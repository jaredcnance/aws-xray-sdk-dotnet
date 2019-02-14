//-----------------------------------------------------------------------------
// <copyright file="DbCommandInterceptor.cs" company="Amazon.com">
//      Copyright 2016 Amazon.com, Inc. or its affiliates. All Rights Reserved.
//
//      Licensed under the Apache License, Version 2.0 (the "License").
//      You may not use this file except in compliance with the License.
//      A copy of the License is located at
//
//      http://aws.amazon.com/apache2.0
//
//      or in the "license" file accompanying this file. This file is distributed
//      on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either
//      express or implied. See the License for the specific language governing
//      permissions and limitations under the License.
// </copyright>
//-----------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Amazon.XRay.Recorder.Core;

namespace Amazon.XRay.Recorder.Handlers.SqlServer
{
    public interface IDbCommandInterceptor
    {
        Task<TResult> InterceptAsync<TResult>(Func<Task<TResult>> method, DbCommand command);
        TResult Intercept<TResult>(Func<TResult> method, DbCommand command);
    }

    public class DbCommandInterceptor : IDbCommandInterceptor
    {
        private const string DataBaseTypeString = "sqlserver";
        private readonly AWSXRayRecorder _recorder;
        private readonly bool? _collectSqlQueriesOverride;        

        public DbCommandInterceptor(AWSXRayRecorder recorder, bool? collectSqlQueries = null)
        {
            _recorder = recorder;
            _collectSqlQueriesOverride = collectSqlQueries;
        }

        public async Task<TResult> InterceptAsync<TResult>(Func<Task<TResult>> method, DbCommand command)
        {
            _recorder.BeginSubsegment(BuildSegmentName(command));
            try
            {
                _recorder.SetNamespace("remote");
                var ret = await method();
                CollectSqlInformation(command);

                return ret;
            }
            catch (Exception e)
            {
                _recorder.AddException(e);
                throw;
            }
            finally
            {
                _recorder.EndSubsegment();
            }
        }

        public TResult Intercept<TResult>(Func<TResult> method, DbCommand command)
        {
            _recorder.BeginSubsegment(BuildSegmentName(command));
            try
            {
                _recorder.SetNamespace("remote");
                var ret = method();
                CollectSqlInformation(command);

                return ret;
            }
            catch (Exception e)
            {
                _recorder.AddException(e);
                throw;
            }
            finally
            {
                _recorder.EndSubsegment();
            }
        }

        protected virtual void CollectSqlInformation(DbCommand command)
        {
            _recorder.AddSqlInformation("database_type", DataBaseTypeString);

            _recorder.AddSqlInformation("database_version", command.Connection.ServerVersion);

            SqlConnectionStringBuilder connectionStringBuilder = new SqlConnectionStringBuilder(command.Connection.ConnectionString);

            // Remove sensitive information from connection string
            connectionStringBuilder.Remove("Password");

            _recorder.AddSqlInformation("user", connectionStringBuilder.UserID);
            _recorder.AddSqlInformation("connection_string", connectionStringBuilder.ToString());

            if(ShouldCollectSqlText()) 
            {
                _recorder.AddSqlInformation("sanitized_query", command.CommandText);
            }
        }

        private string BuildSegmentName(DbCommand command) 
            => command.Connection.Database + "@" + SqlUtil.RemovePortNumberFromDataSource(command.Connection.DataSource);

        private bool ShouldCollectSqlText() 
            => _collectSqlQueriesOverride ?? _recorder.XRayOptions.CollectSqlQueries;
    }
}