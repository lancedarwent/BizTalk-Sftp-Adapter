using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Blogical.Shared.Adapters.Common;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Blogical.Shared.Adapters.Sftp.SharpSsh
{
    /// <summary>
    /// SharpSsh sftp wrapper class
    /// </summary>
    public class Sftp : ISftp
    {
        #region Private Members
        private readonly IProducerConsumerCollection<ApplicationStorage> _applicationStorage;
        private const int Totallifetime = 600; // total number of seconds for reusing of connection
        private const int Totaltimediff = 4; // total number of seconds in difference between servers 
        private DateTime _connectedSince;
        private SftpClient _sftp;
        private readonly string _identityFile;
        private readonly string _host;
        private readonly string _user = String.Empty;
        private readonly string _password;
        private readonly string _passphrase = String.Empty;

        // Proxy Settings
        private readonly string _proxyHost = string.Empty;

        #endregion
        #region ISftp Members
        public bool DebugTrace { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="host">eg 127.0.0.1</param>
        /// <param name="user">username</param>
        /// <param name="password">password (usualy not used together with identityFile)</param>
        /// <param name="identityFile">filename with path. eg. c:\temp\myFile.key</param>
        /// <param name="identityThumbprint">Thumbprint used to locate a X.509 cert in the personal store</param>
        /// <param name="port">Port to use for connection.</param>
        /// <param name="passphrase">passphrase for identityfile.</param>
        /// <param name="debugTrace"></param>
        /// <created>2007-04-01 - Mikael Hkansson</created>
        /// <history>2007-10-11 - Mikael Hkansson, Added code head to all public methods</history>
        /// <history>2008-11-23 - Johan Hedberg, Added passphrase</history>
        /// <history>2013-11-10 - Greg Sharp, Various: preserve stack trace on error, allow reconnection attempts, add X.509 identity certificate support</history>
        public Sftp(string host, string user, string password, string identityFile, string identityThumbprint, int port, string passphrase, bool debugTrace) : 
            this(host, user, password, identityFile, identityThumbprint, port, passphrase, debugTrace, string.Empty, 80, string.Empty, string.Empty)
        {
        }
        public Sftp(string host,
            string user,
            string password,
            string identityFile,
            string identityThumbprint,
            int port,
            string passphrase,
            bool debugTrace,
            string proxyHost,
            int proxyPort,
            string proxyUserName,
            string proxyPassword)
        {
            _applicationStorage = ApplicationStorageHelper.Load();

            var connectionInfo = !string.IsNullOrEmpty(_proxyHost)
                ? new ConnectionInfo(host, port, user, ProxyTypes.Socks5, proxyHost, proxyPort, proxyUserName,
                    proxyPassword)
                : new ConnectionInfo(host, port, user);

            if (string.IsNullOrEmpty(identityFile))
            {
                if (!string.IsNullOrEmpty(password))
                    _sftp.ConnectionInfo.AuthenticationMethods.Add(new PasswordAuthenticationMethod(user, password));
            }
            else
            {
                _sftp.ConnectionInfo.AuthenticationMethods.Add(
                    !String.IsNullOrEmpty(_passphrase)
                        ? new PrivateKeyAuthenticationMethod(_user, new PrivateKeyFile(_identityFile, _passphrase))
                        : new PrivateKeyAuthenticationMethod(_user, new PrivateKeyFile(_identityFile)));
                //else if (!String.IsNullOrEmpty(this._identityThumbprint))
                //_sftp.AddIdentityCert(this._identityThumbprint); 
            }

            _sftp = new SftpClient(connectionInfo);
            _sftp.HostKeyReceived += CheckHostKey;
            _identityFile = identityFile;
            _host = host;
            _user = user;
            _password = password;
            _passphrase = passphrase;
            DebugTrace = debugTrace;
            _proxyHost = proxyHost;
        }



        /// <summary>
        /// Used for receiving files
        /// </summary>
        /// <param name="fromFilePath"></param>
        /// <returns></returns>
        public Stream Get(string fromFilePath)
        {
            try
            {
                try
                {
                    Connect();
                    return _sftp.OpenRead(fromFilePath);
                }
                catch
                {
                    ReConnect();
                    return _sftp.OpenRead(fromFilePath);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionHandling.HandleComponentException(
                    EventLogEventIDs.UnableToGetFile,
                    System.Reflection.MethodBase.GetCurrentMethod(),
                    new SftpException("Unable to get file " + fromFilePath, ex));
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }
        /// <summary>
        /// Used for sending files
        /// </summary>
        /// <param name="memStream"></param>
        /// <param name="destination"></param>
        public void Put(Stream memStream, string destination)
        {
            try
            {
                try
                {
                    Connect();
                    _sftp.UploadFile(memStream, destination);
                }
                catch
                {
                    if (memStream.CanSeek && memStream.Position > 0)
                    {
                        memStream.Position = 0;
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw ExceptionHandling.HandleComponentException(
                    EventLogEventIDs.UnableToWriteFile,
                    System.Reflection.MethodBase.GetCurrentMethod(),
                    new SftpException("Unable write file to " + destination, ex));
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }
        /// <summary>
        /// used for renaming files
        /// </summary>
        /// <param name="oldName"></param>
        /// <param name="newName"></param>
        public void Rename(string oldName, string newName)
        {
            try
            {
                try
                {
                    Connect();
                    _sftp.RenameFile(oldName, newName);
                }
                catch
                {
                    ReConnect();
                    _sftp.RenameFile(oldName, newName);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionHandling.HandleComponentException(
                    EventLogEventIDs.UnableToRenameFile,
                    System.Reflection.MethodBase.GetCurrentMethod(),
                    new SftpException("Unable to rename " + oldName + " to " + newName, ex));
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }

        /// <summary>
        /// Returns a list of files (and sizes) from a uri
        /// </summary>
        /// <param name="fileMask"></param>
        /// <param name="uri"></param>
        /// <param name="filesInProcess"></param>
        /// <param name="trace"></param>
        /// <returns></returns>
        public List<FileEntry> Dir(string fileMask, string uri, IList<string> filesInProcess, bool trace)
        {
            try
            {
                try
                {
                    Connect();
                    return InternalDir(fileMask, uri, 0, filesInProcess, trace);
                }
                catch
                {
                    ReConnect();
                    return InternalDir(fileMask, uri, 0, filesInProcess, trace);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionHandling.HandleComponentException(
                   EventLogEventIDs.UnableToListDirectory,
                   System.Reflection.MethodBase.GetCurrentMethod(), ex);
            }
            finally
            {
                RaiseOnDisconnect();
            }

        }

        /// <summary>
        /// Returns a list of files (and sizes) from a uri
        /// </summary>
        /// <param name="fileMask"></param>
        /// <param name="uri"></param>
        /// <param name="maxNumberOfFiles"></param>
        /// <param name="filesInProcess"></param>
        /// <param name="trace"></param>
        /// <returns></returns>
        public List<FileEntry> Dir(string fileMask, string uri, int maxNumberOfFiles, IList<string> filesInProcess, bool trace)
        {
            try
            {
                try
                {
                    Connect();
                    return InternalDir(fileMask, uri, maxNumberOfFiles, filesInProcess, trace);
                }
                catch
                {
                    ReConnect();
                    return InternalDir(fileMask, uri, maxNumberOfFiles, filesInProcess, trace);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionHandling.HandleComponentException(
                    EventLogEventIDs.UnableToListDirectory,
                    System.Reflection.MethodBase.GetCurrentMethod(), ex);
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }
        /// <summary>
        /// Determines wether a specified directory has files.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public bool Exists(string fileName)
        {
            return _sftp.Exists(fileName);
        }
        /// <summary>
        /// Deletes a file
        /// </summary>
        /// <param name="filePath"></param>
        public void Delete(string filePath)
        {
            try
            {
                try
                {
                    Connect();
                    _sftp.Delete(filePath);
                }
                catch
                {
                    ReConnect();
                    _sftp.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionHandling.HandleComponentException(System.Reflection.MethodBase.GetCurrentMethod(), new SftpException("Unable to delete file [" + filePath + "].", ex));
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }
        /// <summary>
        /// Disconnects from sever
        /// </summary>
        public void Disconnect()
        {
            if (DebugTrace)
                Trace.WriteLine("[SftpConnectionPool] Disconnecting from " + _host);
            try
            {
                if (_sftp.IsConnected)
                {
                    _sftp.Disconnect();
                    _sftp = new SftpClient(_host, _user, _password);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionHandling.HandleComponentException(System.Reflection.MethodBase.GetCurrentMethod(), ex);
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }
        /// <summary>
        /// A numerical representing a permission matrix. These permissions are overridden on Windows platforms, and are therefore useless on such a host. Default value on UNIX platforms are 644. If left empty, no permissioins will be applied.
        /// </summary>
        /// <param name="permissions"></param>
        /// <param name="filePath"></param>
        public void ApplySecurityPermissions(string permissions, string filePath)
        {
            // Don't apply if empty 
            if (String.IsNullOrEmpty(permissions))
                return;

            try
            {
                byte[] buffer = Encoding.Default.GetBytes(permissions);
                int perm = 0;
                foreach (byte t in buffer)
                {
                    int currentPos = t;
                    if (currentPos < '0' || currentPos > '7')
                    {
                        perm = -1;
                        break;
                    }

                    perm <<= 3;
                    perm |= currentPos - '0';
                }

                _sftp.ChangePermissions(filePath, (short)perm);
            }
            catch { throw new Exception("Unable to parse permissions to integer"); }
        }
        #endregion
        #region Private Methods

        /// <summary>
        /// Open an ssh connection
        /// </summary>
        public void Connect()
        {
            try
            {
                if (!_sftp.IsConnected)
                {
                    if (DebugTrace)
                        Trace.WriteLine("[SftpConnectionPool] Connecting to " + _host);




                    _sftp.Connect();

                    //Make sure HostKey match previously retrieved HostKey.
                    _connectedSince = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                if (_sftp.IsConnected)
                    Disconnect();

                throw ExceptionHandling.HandleComponentException(
                    EventLogEventIDs.UnableToConnectToHost,
                    System.Reflection.MethodBase.GetCurrentMethod(),
                    new Exception("Unable to connect to Sftp host [" + _host + "]", ex));
            }
        }
        private void ReConnect()
        {
            Disconnect();
            Trace.WriteLine("[SftpConnectionPool] Reconnecting to " + _host);
            Connect();
        }

        private void RaiseOnDisconnect()
        {
            TimeSpan ts = DateTime.Now.Subtract(_connectedSince);
            if (ts.TotalSeconds > Totallifetime)
            {
                if (_sftp.IsConnected)
                    Disconnect();

                //this.connect();

                Trace.WriteLine("[SftpConnectionPool] Connection has timed out");

            }

        }

        private void CheckHostKey(object sender, HostKeyEventArgs hostKeyEventArgs)
        {
            object hostKey = ApplicationStorageHelper.GetHostKey(_applicationStorage, _host);

            if (hostKey == null)
            {
                _applicationStorage.TryAdd(new ApplicationStorage(_host,
                    Convert.ToBase64String(hostKeyEventArgs.HostKey, 0, hostKeyEventArgs.HostKey.Length)));
                ApplicationStorageHelper.Save(_applicationStorage);
            }
            else if (hostKey.ToString() != Convert.ToBase64String(hostKeyEventArgs.HostKey, 0, hostKeyEventArgs.HostKey.Length))
                throw ExceptionHandling.HandleComponentException(System.Reflection.MethodBase.GetCurrentMethod(),
                       new Exception("HostKey does not match previously retrieved HostKey."));
        }
        private List<FileEntry> InternalDir(string fileMask, string uri, int maxNumberOfFiles, IList<string> filesInProcess, bool trace)
        {
            try
            {
                if (trace)
                    Trace.WriteLine("[SftpReceiverEndpoint] Dir(" + fileMask + ")");

                List<FileEntry> fileEntries = new List<FileEntry>();

                foreach(var entry in _sftp.ListDirectory(fileMask))
                {
                    string remotePath = Path.GetDirectoryName(fileMask);
                    long size = entry.Attributes.Size;
                    bool isDirectory = entry.Attributes.IsDirectory;
                    string fileName = entry.Name;
                    DateTime fileLastWriten;

                    if (isDirectory || size == 0)
                        continue;

                    try
                    {
                        fileLastWriten = entry.Attributes.LastWriteTime;
                    }
                    catch { fileLastWriten = DateTime.Now.AddMinutes(1); }

                    TimeSpan ts = DateTime.Now.Subtract(fileLastWriten);

                    try
                    {
                        if (ts.TotalSeconds > Totaltimediff &&
                            !filesInProcess.Contains(CommonFunctions.CombinePath(remotePath, fileName)))
                        {
                            // "Check out" file if UseLoadBalancing == true 
                            if (uri != null && !DataBaseHelper.CheckOutFile(uri, Environment.MachineName, fileName, trace))
                                continue; // loadbalancing==on, checked out failed
                            else if (uri != null)// loadbalancing==on, checked out succeeded
                            {
                                // Check that file still exists
                                string filePath = fileMask.Substring(0, fileMask.LastIndexOf("/") + 1) + fileName;

                                if (_sftp.Exists(filePath))
                                    fileEntries.Add(new FileEntry(fileName, size));
                                else
                                {
                                    DataBaseHelper.CheckInFile(uri, fileName, trace);
                                    continue;
                                }
                            }
                            else //No loadbalancing
                            {
                                fileEntries.Add(new FileEntry(fileName, size));
                            }
                        }
                    }
                    catch
                    {
                        if (uri != null)
                            DataBaseHelper.CheckInFile(uri, fileName, trace);
                    }
                    if (fileEntries.Count == maxNumberOfFiles && maxNumberOfFiles > 0)
                        break;

                }
                if (trace)
                    Trace.WriteLine(string.Format("[SftpReceiverEndpoint] Found {0} files.", fileEntries.Count));
                return fileEntries;
            }
            catch (Exception ex)
            {
                throw new SftpException("Unable to perform directory list at [" + uri + "]", ex);
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }
        /// <summary>
        /// This method is no longer used, but is left beacuase it might be used in the future. 
        /// Developed to create a more efficient directory listing. By creating a random order of files, 
        /// listing large amount of files from different different BizTalk nodes became more efficient.
        /// </summary>
        /// <param name="fileMask"></param>
        /// <param name="uri"></param>
        /// <param name="maxNumberOfFiles"></param>
        /// <param name="filesInProcess"></param>
        /// <param name="trace"></param>
        /// <returns></returns>
        private List<FileEntry> RandomDir(string fileMask, string uri, int maxNumberOfFiles, ArrayList filesInProcess, bool trace)
        {
            List<FileEntry> fileEntries = new List<FileEntry>();

            try
            {
                var entries = _sftp.ListDirectory(fileMask).ToList();

                Random autoRand = new Random();

                while (entries.Count > 0 && (fileEntries.Count < maxNumberOfFiles || maxNumberOfFiles == 0))
                {
                    var index = autoRand.Next(entries.Count - 1);
                    var entry = entries[index];

                    var remotePath = Path.GetDirectoryName(fileMask);
                    var size = entry.Attributes.Size;
                    var isDirectory = entry.Attributes.IsDirectory;
                    var fileName = entry.Name;

                    if (isDirectory)
                    {
                        entries.RemoveAt(index);
                        continue;
                    }

                    DateTime fileLastWriten;
                    try
                    {
                        fileLastWriten = entry.Attributes.LastWriteTime;
                    }
                    catch { fileLastWriten = DateTime.Now.AddMinutes(1); }
                    var ts = DateTime.Now.Subtract(fileLastWriten);

                    try
                    {
                        if (ts.TotalSeconds > Totaltimediff &&
                            !filesInProcess.Contains(CommonFunctions.CombinePath(remotePath, fileName)))
                        {
                            // "Check out" file if UseLoadBalancing == true 
                            if (uri != null && !DataBaseHelper.CheckOutFile(uri, Environment.MachineName, fileName, trace))
                            {
                                entries.RemoveAt(index);
                                continue;
                            }
                            else
                            {
                                // Check that file still exists
                                string filePath = fileMask.Substring(0, fileMask.LastIndexOf("/") + 1) + fileName;
                                if (_sftp.Exists(filePath))
                                    fileEntries.Add(new FileEntry(fileName, size));
                                else
                                {
                                    if (uri != null)
                                        DataBaseHelper.CheckInFile(uri, fileName, trace);
                                }
                            }
                        }
                    }
                    catch
                    {
                        if (uri != null)
                            DataBaseHelper.CheckInFile(uri, fileName, trace);
                    }
                    entries.RemoveAt(index);
                }

                Trace.WriteLine(string.Format("[SftpReceiverEndpoint] Found {0} files.", fileEntries.Count));
                return fileEntries;
            }
            catch (Exception ex)
            {
                throw new SftpException("Unable to perform directory list", ex);
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }

        /// <summary>
        /// Gets information about a file.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="trace"></param>
        /// <returns></returns>
        public FileEntry GetFileEntry(string filePath, bool trace)
        {
            try
            {
                if (trace)
                    Trace.WriteLine("[SftpReceiverEndpoint] GetFileEntry(" + filePath + ")");

                foreach (var entry in _sftp.ListDirectory(filePath))
                {
                    long size = entry.Attributes.Size;
                    string fileName = entry.Name;
                    return new FileEntry(fileName, size);
                }

                if (trace)
                    Trace.WriteLine("[SftpReceiverEndpoint] File not Found \"" + filePath + "\".");

                throw new SftpException("File not Found \"" + filePath + "\".");
            }
            catch (Exception ex)
            {
                throw new SftpException("Unable to perform directory list for \"" + filePath + "\".", ex);
            }
            finally
            {
                RaiseOnDisconnect();
            }
        }

        #region IDisposable Support
        private bool _disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _sftp.Dispose();
                }

                _disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
        #endregion
    }

}
