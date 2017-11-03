using System;
using System.Collections.Generic;
using System.Text;
using System.IO.IsolatedStorage;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

using System.Collections.Concurrent;
using System.Linq;
using System.Xml.Serialization;

namespace Blogical.Shared.Adapters.Sftp
{
    /// <summary>
    /// Used for storing HostKeys in IsolatedStorage
    /// </summary>
    [Serializable]
    public class ApplicationStorage 
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public ApplicationStorage() { }
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="host"></param>
        /// <param name="hostKey"></param>
        public ApplicationStorage(string host, string hostKey)
        {
            Host = host;
            HostKey = hostKey;
        }
        /// <summary>
        /// Name of the application host or server
        /// </summary>
        public string Host;
        /// <summary>
        /// Unique id retrieved from server
        /// </summary>
        public string HostKey;
    }
    /// <summary>
    /// 
    /// </summary>
    internal static class ApplicationStorageHelper
    {
        const string settingsFileName = "SftpHostFiles.config";
        const string objlock = "lock";
        /// <summary>
        /// Load all hostkeys from IsolatedStorage.
        /// Eg. \Document and Settings\[BizTalk Service User]\Local Settings\Application Data\IsolatedStorage\
        /// </summary>
        /// <returns></returns>
        public static IProducerConsumerCollection<ApplicationStorage> Load()
        {
            IsolatedStorageFile isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
            var ApplicationStorage = new ConcurrentBag<ApplicationStorage>();
            if (isoStore.GetFileNames(settingsFileName).Length == 0)
            {
                return ApplicationStorage;
            }

            // Read the stream from Isolated Storage.
            lock (objlock)
            {
                Stream stream = new IsolatedStorageFileStream(settingsFileName, FileMode.OpenOrCreate, isoStore);
                if (stream != null)
                {
                    try
                    {
                        XmlSerializer ser = new XmlSerializer(typeof(ApplicationStorage[]));
                        TextReader reader = new StreamReader(stream);
                        ApplicationStorage[] arr = (ApplicationStorage[])ser.Deserialize(reader);
                        ApplicationStorage = new ConcurrentBag<ApplicationStorage>();
                        reader.Close();
                    }
                    finally
                    {
                        // We are done with it.
                        stream.Close();
                    }
                }
            }
            return ApplicationStorage;
        }

        public static IProducerConsumerCollection<ApplicationStorage> _Load()
        {
            IsolatedStorageFile isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
            var ApplicationStorage = new ConcurrentBag<ApplicationStorage>();
            if (isoStore.GetFileNames(settingsFileName).Length == 0)
            {
                return ApplicationStorage;
            }

            // Read the stream from Isolated Storage.
            Stream stream = new IsolatedStorageFileStream(settingsFileName, FileMode.OpenOrCreate, isoStore);
            if (stream != null)
            {
                try
                {
                    XmlSerializer ser = new XmlSerializer(typeof(ApplicationStorage[]));
    				TextReader reader = new StreamReader(stream);
                    ApplicationStorage[]arr= (ApplicationStorage[])ser.Deserialize(reader);
                    ApplicationStorage = new ConcurrentBag<ApplicationStorage>();
    				reader.Close();             
                }
                finally
                {
                    // We are done with it.
                    stream.Close();
                }
            }
            return ApplicationStorage;
        }
        /// <summary>
        /// Save hostkeys to IsolatedStorage
        /// Eg. \Document and Settings\[BizTalk Service User]\Local Settings\Application Data\IsolatedStorage\
        /// </summary>
        /// <param name="applicationStorage"></param>
        public static void Save(IEnumerable<ApplicationStorage> applicationStorage)
        {
            // Open the stream from the IsolatedStorage.
            IsolatedStorageFile isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);

            // Greg Sharp: Provide thread-safety around write access to the config file
            lock (objlock)
            {
                Stream stream = new IsolatedStorageFileStream(settingsFileName, FileMode.Create, isoStore);
                if (stream != null)
                {
                    try
                    {
                        XmlSerializer ser = new XmlSerializer(typeof(ApplicationStorage[]));
                        TextWriter writer = new StreamWriter(stream);
                        ser.Serialize(writer, applicationStorage.ToArray());
                        writer.Close();
                    }
                    finally
                    {
                        stream.Close();
                    }
                }
            }
        }
        /// <summary>
        /// Get the hostkey from application storage
        /// </summary>
        /// <param name="applicationStorage"></param>
        /// <param name="host"></param>
        /// <returns></returns>
        public static string GetHostKey(IEnumerable<ApplicationStorage> applicationStorage, string host)
        {
            foreach (ApplicationStorage apps in applicationStorage)
            {
                if (apps.Host == host)
                    return apps.HostKey;
            }
            return null;
        }
    }


}
