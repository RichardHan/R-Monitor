﻿using log4net;
using log4net.Config;
using System;
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
        static string machineName = Environment.MachineName;
        static string smtpHost = ConfigurationManager.AppSettings["smtpHost"];
        static int smtpPort = int.Parse(ConfigurationManager.AppSettings["smtpPort"]);
        static bool smtpEnableSSL = bool.Parse(ConfigurationManager.AppSettings["smtpEnableSSL"]);
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
        static string smtpCredentialsPassword = ConfigurationManager.AppSettings["smtpCredentialsPassword"];
        static string smtpCredentialsName = ConfigurationManager.AppSettings["smtpCredentialsName"];
        static bool mailEnableSendToDirectory = bool.Parse(ConfigurationManager.AppSettings["mailEnableSendToDirectory"]);
        static string mailPickupDirectoryLocation = ConfigurationManager.AppSettings["mailPickupDirectoryLocation"];
        static string from = ConfigurationManager.AppSettings["from"];
        static string receivers = ConfigurationManager.AppSettings["receivers"];

        static MailMessage message = new MailMessage();
        static SmtpClient smtpClient;
        public static readonly ILog SiteLogger = LogManager.GetLogger("SiteLogger");
        public static readonly ILog DbLogger = LogManager.GetLogger("DbLogger");
        public static readonly ILog DefaultLogger = LogManager.GetLogger("DefaultLogger");
        static void Main(string[] args)
        {
            XmlConfigurator.Configure();

            smtpClient = new SmtpClient()
            {
                Host = smtpHost,
                Port = smtpPort,
                EnableSsl = smtpEnableSSL,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
            };

            if (!string.IsNullOrEmpty(smtpCredentialsName))
            {
                smtpClient.Credentials = new NetworkCredential(smtpCredentialsName, smtpCredentialsPassword);
            }

            message.From = new MailAddress(from);
            foreach (string email in receivers.Split(';'))
            {
                message.To.Add(new MailAddress(email));
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
                                    if (dr.HasRows)
                                    {
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
                            errorMsg = ex.Message.Trim().Replace("\r", "").Replace("\n", "");
                            DefaultLogger.Error(ex);
                            DefaultLogger.Error(ex);
                        }

                        sw.Stop();
                        int totalSpendSec = sw.Elapsed.Seconds;

                        if (totalSpendSec > connectDBTimeout)
                        {
                            isSuccess = false;
                            errorMsg = "Connection timeout. Spend time " + totalSpendSec + "secs more than " + connectDBTimeout + " secs.";
                        }

                        if (isSuccess)
                        {
                            dbAliveHandler(dbserver, db, command, sw.Elapsed);
                        }
                        else
                        {
                            dbErrorHandler(dbserver, db, command, sw.Elapsed, errorMsg);
                        }
                    }
                }

                string urls_value = ConfigurationManager.AppSettings["URLs"];

                if (!string.IsNullOrEmpty(urls_value))
                {
                    string[] urls = urls_value.Split(new string[] { "(@)" }, StringSplitOptions.RemoveEmptyEntries);

                    for (int i = 0; i < urls.Length; i++)
                    {
                        urls[i] = urls[i].Trim()
                                         .Replace("&lt;", "<")
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
                            siteDownHandler(url, ex.Message.Trim().Replace("\r", "").Replace("\n", ""));
                        }
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
                    siteDownHandler(url, "Response is null Or StatusCode not equals 200.");
                }
                else
                {
                    siteAliveHandler(url);
                }
            }
            catch (Exception ex)
            {
                siteDownHandler(url, ex.Message.Trim().Replace("\r", "").Replace("\n", ""));
            }
            finally
            {
                request.Abort();
            }
        }

        private static void networkDownHandler()
        {
            SendEmail(machineName + " network not available", "" + Environment.NewLine);
            DefaultLogger.Error(new Exception("Detecting Network not available."));
            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Down] network");
            Console.ResetColor();
        }

        private static void SendEmail(string subject, string body)
        {
            message.Subject = subject;
            message.Body = body;

            if (enableSendMailWhenError)
            {
                try
                {
                    if (mailEnableSendToDirectory)
                    {
                        if (!Directory.Exists(mailPickupDirectoryLocation))
                        {
                            Console.WriteLine(mailPickupDirectoryLocation + " directory not exists, auto create it.");
                            Directory.CreateDirectory(mailPickupDirectoryLocation);
                        }
                        smtpClient.PickupDirectoryLocation = mailPickupDirectoryLocation;
                        smtpClient.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.SpecifiedPickupDirectory;
                        smtpClient.EnableSsl = false;
                    }

                    smtpClient.Send(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    DefaultLogger.Error(ex);
                }
            }
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
            SiteLogger.Info("Down" + "," + url.Trim() + "," + errorMsg);
            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Down]" + url.Trim() + "  Error:" + errorMsg);
            Console.ResetColor();
            SendEmail(mailsubjectPrefix + url + " is down", "[" + machineName + "]" + Environment.NewLine + url.Trim() + Environment.NewLine + errorMsg);
        }

        private static void siteAliveHandler(string url)
        {
            if (isSaveLiveLog)
            {
                SiteLogger.Info("Up" + "," + url.Trim());
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
                DbLogger.Info("Up" + "," + server + "," + db + "," + ts.TotalSeconds + "," + command);
            }

            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[DB Success]" + server + " " + db + " " + ts.ToString());
            Console.ResetColor();
        }

        private static void dbErrorHandler(string server, string db, string command, TimeSpan ts, string errMsg)
        {
            String body =
                "[" + machineName + "]" + Environment.NewLine
                + server + Environment.NewLine
                + command + Environment.NewLine
                + errMsg + Environment.NewLine + ts.ToString();

            SendEmail(mailsubjectPrefix + "Connect to " + server + " " + db + " fail", body);

            DbLogger.Info("Down" + "," + server + "," + db + "," + ts.TotalSeconds + "," + command + "," + errMsg);
            Type type = typeof(ConsoleColor);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[DB Fail]" + server + " " + db + " " + ts.ToString());
            Console.ResetColor();
        }
    }
}