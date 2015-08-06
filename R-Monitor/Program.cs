using System;
using System.Collections;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace R_Monitor
{
    class Program
    {
        static string machineName = System.Environment.MachineName;
        static string smtpHost = ConfigurationManager.AppSettings["smtpHost"];
        static MailMessage message = new MailMessage();

        static SmtpClient smtp = new SmtpClient()
        {
            Host = "smtp.gmail.com",
            Port = 587,
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(from, ConfigurationManager.AppSettings["from_password"])            
        };

        static int sleepSecs = string.IsNullOrEmpty(ConfigurationManager.AppSettings["sleepSecs"]) ?
            30 : int.Parse(ConfigurationManager.AppSettings["sleepSecs"]);

        static int requestTimeout = string.IsNullOrEmpty(ConfigurationManager.AppSettings["requestTimeout"]) ?
            5000 : int.Parse(ConfigurationManager.AppSettings["requestTimeout"]);

        static bool repeat = string.IsNullOrEmpty(ConfigurationManager.AppSettings["repeat"]) ?
            true : bool.Parse(ConfigurationManager.AppSettings["repeat"]);

        static int connectDBTimeout = string.IsNullOrEmpty(ConfigurationManager.AppSettings["connectDBTimeout"]) ?
            3 : int.Parse(ConfigurationManager.AppSettings["connectDBTimeout"]);

        static bool enableSendMailWhenError = string.IsNullOrEmpty(ConfigurationManager.AppSettings["enableSendMailWhenError"]) ?
            true : ConfigurationManager.AppSettings["enableSendMailWhenError"] == "true";

        static bool isSaveLiveLog = string.IsNullOrEmpty(ConfigurationManager.AppSettings["saveLiveLog"]) ?
            true : ConfigurationManager.AppSettings["saveLiveLog"] == "true";

        static string mailsubjectPrefix = string.IsNullOrEmpty(ConfigurationManager.AppSettings["mailsubjectPrefix"]) ?
            "[R-monitor]" : ConfigurationManager.AppSettings["mailsubjectPrefix"];

        static string connectionsandCommands = ConfigurationManager.AppSettings["connectionsandCommands"];
        static string from = ConfigurationManager.AppSettings["from"];
        static string receivers = ConfigurationManager.AppSettings["receivers"];

        static string SiteLiveLogFileName = "SiteLiveLogs.csv";
        static string DBSiteLogFileName = "DBLiveLogs.csv";
        static string NetworkLogFileName = "NetworkDownLogs.csv";
        static string DBErrorLogFileName = "DBErrorLogs.csv";
        static string SiteDownLogFileName = "SiteDownLogs.csv";

        static void Main(string[] args)
        {
            message.From = new MailAddress(from);
            foreach (string email in receivers.Split(';'))
            {
                message.To.Add(email);
            }
            
            while (true)
            {
                bool network = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                if (network)
                {
                    networkAliveHandler();
                }
                else
                {
                    networkDownHandler();
                }

                foreach (var connStringandCommand in connectionsandCommands.Split(new string[] { "|||||" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    bool isSuccess = true;
                    string errorMsg = "";
                    string connString = connStringandCommand.Trim().Split(',')[0];
                    string command = connStringandCommand.Trim().Split(',')[1];
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    using (SqlConnection conn = new SqlConnection(connString))
                    {
                        string dbserver = conn.DataSource;
                        string db = conn.Database;
                        try
                        {
                            SqlCommand cmd = conn.CreateCommand();
                            cmd.CommandText = command;
                            conn.Open();
                            if (conn.State.ToString() == "Open")
                            {
                                using (SqlDataReader dr = cmd.ExecuteReader())
                                {
                                    ArrayList alRows = new ArrayList();
                                    if (dr.HasRows)
                                    {

                                        foreach (System.Data.Common.DbDataRecord r in dr)
                                        {
                                            alRows.Add(r);
                                        }

                                        isSuccess = true;
                                    }
                                    else
                                    {
                                        isSuccess = false;
                                        errorMsg = "ExecuteReader reutrn 0 rows.";
                                    }
                                }
                            }
                            else
                            {
                                isSuccess = false;
                                errorMsg = "Connection status is not Open";
                            }
                        }
                        catch (Exception ex)
                        {
                            isSuccess = false;
                            errorMsg = ex.Message;
                        }

                        sw.Stop();

                        if (sw.Elapsed.Seconds > connectDBTimeout)
                        {
                            isSuccess = false;
                            errorMsg = "Connection timeout. Spend time more than 3 secs.";
                        }

                        if (isSuccess)
                        {
                            dbAliveHandler(dbserver, db, command, sw.Elapsed);
                        }
                        else
                        {
                            dbErrorHandler(dbserver, db, command, errorMsg, sw.Elapsed);
                        }
                    }
                }

                string urls_value = ConfigurationManager.AppSettings["URLs"];
                string[] urls = urls_value.Split(new string[] { "(@)" }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < urls.Length; i++)
                {
                    urls[i] = urls[i].Trim();
                    urls[i] = urls[i].Replace("&lt;", "<")
                                                   .Replace("&amp;", "&")
                                                   .Replace("&gt;", ">")
                                                   .Replace("&quot;", "\"")
                                                   .Replace("&apos;", "'");

                    urls[i] = HttpUtility.UrlDecode(urls[i]);
                }

                foreach (string url in urls)
                {
                    try
                    {
                        string strRegex = @"timeout=(.*)";
                        Regex timeoutRegex = new Regex(strRegex, RegexOptions.None);
                        string strTargetString = url;
                        foreach (Match timeoutMatch in timeoutRegex.Matches(strTargetString))
                        {
                            if (timeoutMatch.Success)
                            {
                                int.TryParse(timeoutMatch.Groups[1].Value, out requestTimeout);
                            }
                        }
                        var result = CheckURLAsync(url, requestTimeout);
                    }
                    catch (Exception ex)
                    {
                        siteDownHandler(url, ex.Message);
                    }
                }

                if (repeat == false)
                {
                    Thread.Sleep(30000);
                    break;
                }

                Console.WriteLine("Start next round in " + sleepSecs + " secs...");
                Thread.Sleep(sleepSecs * 1000);
            }
        }

        static async Task CheckURLAsync(string url, int rt)
        {
            WebRequest request = WebRequest.Create(url);
            try
            {
                request.Timeout = rt;
                request.UseDefaultCredentials = true;
                ((HttpWebRequest)request).UserAgent = "Mozilla/4.0 (compatible; MSIE 7.0; Windows NT 5.1)";

                HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync();

                if (response == null || response.StatusCode != HttpStatusCode.OK)
                {
                    siteDownHandler(url, "");
                }
                else
                {
                    siteAliveHandler(url);
                }
            }
            catch (Exception ex)
            {
                siteDownHandler(url, ex.Message);
            }
            finally
            {
                request.Abort();
            }
        }

        private static void networkDownHandler()
        {
            message.Subject = machineName + "network is down";
            message.Body = machineName + "network is down" + Environment.NewLine;

            if (enableSendMailWhenError)
                smtp.Send(message);

            //write log to scv.
            using (StreamWriter outfile = new StreamWriter(NetworkLogFileName, true))
            {
                outfile.WriteLine(DateTime.UtcNow.AddHours(8).ToString("G") + "," + "network" + "," + "");
            }

            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Down] network");
            Console.ResetColor();
        }

        private static void networkAliveHandler()
        {
            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[Alive] network");
            Console.ResetColor();
        }

        private static void siteDownHandler(string url, string errorMsg)
        {
            message.Subject = mailsubjectPrefix + url + " is down";
            message.Body = "[" + machineName + "]" + Environment.NewLine + url.Trim() + Environment.NewLine + errorMsg;

            //write log to scv.
            using (StreamWriter outfile = new StreamWriter(SiteDownLogFileName, true))
            {
                outfile.WriteLine(DateTime.UtcNow.AddHours(8).ToString("G") + "," + url.Trim()
                    + "," + errorMsg.Trim().Replace("\r", "").Replace("\n", ""));
            }

            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Down]" + url.Trim() + "  Error:" + errorMsg);
            Console.ResetColor();

            if (enableSendMailWhenError)
                smtp.Send(message);
        }

        private static void siteAliveHandler(string url)
        {
            if (isSaveLiveLog)
            {
                using (StreamWriter webLiveWriter = new StreamWriter(SiteLiveLogFileName, true))
                {
                    webLiveWriter.WriteLine(DateTime.UtcNow.AddHours(8).ToString("G") + "," + url.Trim());
                }
            }

            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[Alive]" + url.Trim());
            Console.ResetColor();
        }

        private static void dbAliveHandler(string server, string db, string command, TimeSpan ts)
        {
            if (isSaveLiveLog)
            {
                using (StreamWriter dbLiveWriter = new StreamWriter(DBSiteLogFileName, true))
                {
                    dbLiveWriter.WriteLine(DateTime.UtcNow.AddHours(8).ToString("G") + "," + server + "," + db + "," + ts.ToString() + "," + command);
                }
            }

            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[DB Success]" + server + " " + db + " " + ts.ToString());
            Console.ResetColor();
        }

        private static void dbErrorHandler(string server, string db, string command, string errMsg, TimeSpan ts)
        {
            message.Subject = mailsubjectPrefix + "Connect to " + server + " " + db + " fail";
            message.From = new MailAddress(from);

            message.Body =
                "[" + machineName + "]" + Environment.NewLine
                + server + Environment.NewLine
                + command + Environment.NewLine
                + errMsg + Environment.NewLine + ts.ToString();

            if (enableSendMailWhenError)
                smtp.Send(message);

            using (StreamWriter outputfile = new StreamWriter(DBErrorLogFileName, true))
            {
                outputfile.WriteLine(DateTime.UtcNow.AddHours(8).ToString("G") + ","
                    + server + "," + db + "," + command + "," + errMsg.Replace("\r", "").Replace("\n", "") + "," + ts.ToString());
            }

            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[DB Fail]" + server + " " + db + " " + ts.ToString());
            Console.ResetColor();
        }
    }
}