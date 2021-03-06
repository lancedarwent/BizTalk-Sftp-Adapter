using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Configuration;

namespace Blogical.Shared.Adapters.Sftp
{
    /// <summary>
    /// The DataBaseHelper class is used for controling files received from different 
    /// BizTalk nodes, and is only used when the UseLoadBalancing property is set to true.
    /// </summary>
    internal static class DataBaseHelper
    {
        #region Internal Methods

        /// <summary>
        /// Used for making sure other processes (nodes) are not processing the same file
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="node"></param>
        /// <param name="filename"></param>
        /// <param name="trace"></param>
        /// <returns></returns>
        internal static bool CheckOutFile(string uri, string node, string filename,bool trace)
        {
            
            #region query
            
                string query = @"if(
(select count(*) 
from [dbo].[SftpWorkingProcess] 
where [URI] = @URI
and [FileName] =@FileName
and datediff(minute, [Timestamp], getdate()) <10) =0
)
begin
INSERT INTO [dbo].[SftpWorkingProcess]
           ([URI]
           ,[Node]
           ,[FileName])
     VALUES
           (@URI,@Node,@FileName) 

select 1 as WorkInProcess
end
else
select 0 as WorkInProcess";
            #endregion
                
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            {
                try
                {
                    connection.Open();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("Unable to open the Blogical database used for Load balancing. Information about the load balancing feature can be found in the helpifile. If you do not wish to use load balancing you can disable it on the Receive Location transport properties in the Administration Console.",
                        ex);
                }
                using (var transaction = connection.BeginTransaction("CheckoutFile"))
                {
                    using (SqlCommand command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = query;
                        command.Parameters.AddWithValue("@URI", uri);
                        command.Parameters.AddWithValue("@Node", node);
                        command.Parameters.AddWithValue("@FileName", filename);
                        try
                        {
                            using (SqlDataReader reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    int result = (int)reader["WorkInProcess"];
                                    transaction.Commit();

                                    if (result == 1)
                                    {
                                        if(trace)
                                            Trace.WriteLine("[SftpReceiverEndpoint] Checked Out [" + filename+"]");
    
                                        return true;
                                    }
                                }
                                if(trace)
                                    Trace.WriteLine("[SftpReceiverEndpoint] Unable to check Out [" + filename + "]");
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Used for making sure other processes (nodes) are not processing the same file
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="filename"></param>
        /// <param name="trace"></param>
        internal static void CheckInFile(string uri, string filename, bool trace)
        {
            if(trace)
                Trace.WriteLine("[SftpReceiverEndpoint] CheckInFile [" + filename + "]");
            
            #region query

            string queryFormat = @"delete from [dbo].[SftpWorkingProcess] 
where [URI] = @URI
and [FileName] =@FileName ";
            #endregion

            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            {
                connection.Open();
                try
                {
                    using (SqlCommand command = new SqlCommand(queryFormat, connection))
                    {
                        command.Parameters.AddWithValue("@URI", uri);
                        command.Parameters.AddWithValue("@FileName", filename);
                        command.ExecuteNonQuery();
                    }
                }
                catch
                {
                    if(trace)
                        Trace.WriteLine("[SftpReceiverEndpoint] Error when Checking in file [" + filename + "]");
                }
            }
        }

        internal static IEnumerable<string> GetCheckedOutFiles(string uri)
        {
            #region query
            string queryFormat = @"
select [FileName] 
from [dbo].[SftpWorkingProcess] 
where [URI] = @URI ";
            #endregion
            var files = new List<string>();
            using (SqlConnection connection = new SqlConnection(GetConnectionString()))
            {
                connection.Open();
                
                try
                {
                    using (SqlCommand command = new SqlCommand(queryFormat, connection))
                    {
                        command.Parameters.AddWithValue("@URI", uri);
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                files.Add(reader[0].ToString().ToLower());
                            }
                        }
                    }
                }
                catch
                {
                    return files;
                }
            }
            return files;
        }
        #endregion
        #region Private Methods
        private static string GetConnectionString()
        {
            // This connectionstring doesn't need to be Integrated Security=SSPI, but the user that runs the
            // thread of execution for this service needs to have those rights due to an unfourtunate design
            // in BtsCatalogExplorer. Thus it makes sense that the connectionstring alse be integrated authentication.
            try
            {
                string connectionString = ConfigurationManager.ConnectionStrings["Blogical.Shared.Adapters.Sftp"].ConnectionString;
                return connectionString;
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Unable to find the connection string to the Blogical database used for Load balancing. Information about the load balancing feature can be found in the helpifile. If you do not wish to use load balancing you can disable it on the Receive Location transport properties in the Administration Console.",
                            ex);
            }
        }
        #endregion
    }
}
