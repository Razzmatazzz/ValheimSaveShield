using System;
using System.IO;
using WinSCP;

namespace ValheimSaveShield
{
    public class SynchronizeDirectories
    {
        private static Exception _lasterror;
        public static Exception LastError
        {
            get
            {
                return _lasterror;
            }
        }
        public static int remoteSync(string hostUrl, string port, string hostDirectory, string localDirectory, string userName, string password, FtpMode ftpMode)
        {
            try
            {
                // Setup session options
                SessionOptions sessionOptions = new SessionOptions
                {
                    Protocol = Protocol.Ftp,
                    HostName = hostUrl,
                    PortNumber = Int32.Parse(port),
                    UserName = userName,
                    Password = password,
                    FtpMode = ftpMode
                };

                using (Session session = new Session())
                {
                    // Will continuously report progress of synchronization
                    session.FileTransferred += FileTransferred;

                    // Connect
                    session.Open(sessionOptions);
                    
                    // Synchronize files
                    SynchronizationResult synchronizationResult;
                    synchronizationResult =
                        session.SynchronizeDirectories(
                            SynchronizationMode.Local, localDirectory,
                            hostDirectory, false);

                    // Throw on any error
                    synchronizationResult.Check();
                }

                return 0;
            }
            catch (Exception e)
            {
                _lasterror = e;
                System.Diagnostics.Debug.WriteLine("Error: {0}", e);
                return 1;
            }
        }

        public static int downloadWorlds(string hostUrl, string port, string hostDirectory, string localDirectory, string userName, string password, FtpMode ftpMode)
        {
            try
            {
                // Setup session options
                SessionOptions sessionOptions = new SessionOptions
                {
                    Protocol = Protocol.Ftp,
                    HostName = hostUrl,
                    PortNumber = Int32.Parse(port),
                    UserName = userName,
                    Password = password,
                    FtpMode = ftpMode
                };

                using (Session session = new Session())
                {
                    // Will continuously report progress of synchronization
                    session.FileTransferred += FileTransferred;

                    // Connect
                    session.Open(sessionOptions);

                    //download
                    //transfer .fwl files first since they don't trigger a backup
                    var transferResult = session.GetFilesToDirectory(hostDirectory, localDirectory, "*.fwl");
                    transferResult.Check();
                    //transfer .db files next, but under a different file extension to delay triggering a backup
                    transferResult = session.GetFiles($"{hostDirectory}/*.db", $@"{localDirectory}\*.dbftp");
                    transferResult.Check();
                    foreach (TransferEventArgs transfer in transferResult.Transfers)
                    {
                        //rename finished .db downloads to trigger a backup
                        var dbName = transfer.Destination.Replace(".dbftp", ".db");
                        if (File.Exists(dbName)) File.Delete(dbName);
                        var fi = new FileInfo(transfer.Destination);
                        fi.MoveTo(fi.FullName.Replace(transfer.Destination, dbName));
                    }
                }

                return 0;
            }
            catch (Exception e)
            {
                _lasterror = e;
                System.Diagnostics.Debug.WriteLine("Error: {0}", e);
                return 1;
            }
        }

        private static void FileTransferred(object sender, TransferEventArgs e)
        {
            if (e.Error == null)
            {
                System.Diagnostics.Debug.WriteLine("Upload of {0} succeeded", e.FileName);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Upload of {0} failed: {1}", e.FileName, e.Error);
            }

            if (e.Chmod != null)
            {
                if (e.Chmod.Error == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Permissions of {0} set to {1}", e.Chmod.FileName, e.Chmod.FilePermissions);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Setting permissions of {0} failed: {1}", e.Chmod.FileName, e.Chmod.Error);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Permissions of {0} kept with their defaults", e.Destination);
            }

            if (e.Touch != null)
            {
                if (e.Touch.Error == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Timestamp of {0} set to {1}", e.Touch.FileName, e.Touch.LastWriteTime);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Setting timestamp of {0} failed: {1}", e.Touch.FileName, e.Touch.Error);
                }
            }
            else
            {
                // This should never happen during "local to remote" synchronization
                System.Diagnostics.Debug.WriteLine(
                    "Timestamp of {0} kept with its default (current time)", e.Destination);
            }
        }

        public static void uploadFile(string hostUrl, string port, string hostDirectory, string localFile, string userName, string password, FtpMode ftpMode)
        {
            try
            {
                // Setup session options
                SessionOptions sessionOptions = new SessionOptions
                {
                    Protocol = Protocol.Ftp,
                    HostName = hostUrl,
                    PortNumber = Int32.Parse(port),
                    UserName = userName,
                    Password = password,
                    FtpMode = ftpMode
                };

                using (Session session = new Session())
                {
                    // Will continuously report progress of synchronization
                    session.FileTransferred += FileTransferred;

                    // Connect
                    session.Open(sessionOptions);

                    // Upload
                    var uploadResult = session.PutFileToDirectory(localFile, hostDirectory);

                    // Throw on any error
                    if (uploadResult.Error != null)
                    {
                        throw uploadResult.Error;
                    }
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }
    }
}