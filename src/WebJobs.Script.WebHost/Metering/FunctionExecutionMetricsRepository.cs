// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Scale;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metering
{
    public class FunctionExecutionMetricsRepository : IFunctionExecutionMetricsRepository
    {
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;
        private readonly ILogger<FunctionExecutionMetricsRepository> _logger;
        private FunctionExecutionMeteringOptions _options;
        internal const string TableNamePrefix = "AzureFunctionsExecutionMetrics";
        private const string SampleTimestampPropertyName = "SampleTimestamp";

        private CloudTableClient _tableClient;

        public FunctionExecutionMetricsRepository(IConfiguration configuration, IEnvironment environment,
            IOptions<FunctionExecutionMeteringOptions> meteringOptions,
            ILogger<FunctionExecutionMetricsRepository> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
            _options = meteringOptions.Value;
        }

        internal CloudTableClient TableClient
        {
            get
            {
                if (_tableClient == null)
                {
                    string storageConnectionString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
                    CloudStorageAccount account = null;
                    if (!string.IsNullOrEmpty(storageConnectionString) &&
                        CloudStorageAccount.TryParse(storageConnectionString, out account))
                    {
                        _tableClient = account.CreateCloudTableClient();
                    }
                    else
                    {
                        _logger.LogError("Azure Storage connection string is empty or invalid. Unable to read/write function execution metrics.");
                    }
                }
                return _tableClient;
            }
        }

        public async Task WriteFunctionExecutionMetrics(IEnumerable<TrackedFunctionExecutionActivity> activities)
        {
            if (!activities.Any())
            {
                return;
            }

            try
            {
                var batch = new TableBatchOperation();
                foreach (var activity in activities)
                {
                    AccumulateMetricsBatchAsync(batch, activity);
                }

                await ExecuteBatchSafeAsync(batch);
            }
            catch (StorageException ex)
            {
                _logger.LogError(ex, $"An unhandled storage exception occurred when reading/writing function execution metrics: {ex.ToString()}");
                throw;
            }
        }

        internal async Task ExecuteBatchSafeAsync(TableBatchOperation batch, DateTime? now = null)
        {
            var metricsTable = GetMetricsTable(now);
            if (metricsTable != null && batch.Any())
            {
                try
                {
                    // TODO: handle paging and errors
                    await metricsTable.ExecuteBatchAsync(batch);
                }
                catch (StorageException e)
                {
                    if (IsNotFoundTableNotFound(e))
                    {
                        // create the table and retry
                        await CreateIfNotExistsAsync(metricsTable);
                        await metricsTable.ExecuteBatchAsync(batch);
                        return;
                    }

                    throw e;
                }
            }
        }

        internal CloudTable GetMetricsTable(DateTime? now = null)
        {
            CloudTable table = null;

            if (TableClient != null)
            {
                // we'll roll automatically to a new table once per month
                now = now ?? DateTime.UtcNow;
                string tableName = string.Format("{0}{1:yyyyMM}", TableNamePrefix, now.Value);
                return TableClient.GetTableReference(tableName);
            }

            return table;
        }

        internal void AccumulateMetricsBatchAsync(TableBatchOperation batch, TrackedFunctionExecutionActivity activity, DateTime? now = null)
        {
            var containerName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName);
            var operation = CreateMetricsInsertOperation(activity, containerName, now);
            batch.Add(operation);
        }

        internal static TableOperation CreateMetricsInsertOperation(TrackedFunctionExecutionActivity activity, string containerName, DateTime? now = null)
        {
            now = now ?? DateTime.UtcNow;

            // Use an inverted ticks rowkey to order the table in descending order, allowing us to easily
            // query for latest logs. Adding a guid as part of the key to ensure uniqueness.
            string rowKey = string.Format("{0:D19}-{1}", DateTime.MaxValue.Ticks - now.Value.Ticks, Guid.NewGuid());

            var entity = TableEntityConverter.ToEntity(metrics, containerName, rowKey, metrics.Timestamp);
            entity.Properties.Add(MonitorIdPropertyName, EntityProperty.GeneratePropertyForString(descriptor.Id));

            // We map the sample timestamp to its own column so it doesn't conflict with the built in column.
            // We want to ensure that timestamp values for returned metrics are precise and monotonically
            // increasing when ordered results are returned. The built in timestamp doesn't guarantee this.
            entity.Properties.Add(SampleTimestampPropertyName, EntityProperty.GeneratePropertyForDateTimeOffset(activity.TimeStamp));

            return TableOperation.Insert(entity);
        }

        private static bool IsNotFoundTableNotFound(StorageException exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            var result = exception.RequestInformation;
            if (result == null)
            {
                return false;
            }

            if (result.HttpStatusCode != (int)HttpStatusCode.NotFound)
            {
                return false;
            }

            var extendedInformation = result.ExtendedErrorInformation;
            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "TableNotFound";
        }

    }
}