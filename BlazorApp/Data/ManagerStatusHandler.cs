using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BlazorApp.Data.Events;
using BlazorApp.DataStreaming.Events;
using Microsoft.Data.SqlClient;
using SQLDatabaseRead;

namespace BlazorApp.Data
{
    public class ManagerStatusHandler : EventBase
    {
        private const long MaxMemory = 21473734656; /* Approx 20gb */
        private SQLDependencyListener _healthStreamer;
        private SQLDependencyListener _errorStreamer;
        private SQLDependencyListener _reconciliationStreamer;
        private int _mtRetryCount;
        
        public long AvgCpu;
        public long AvgMemory;
        public readonly List<HealthData> CpuDataList = new ();
        public readonly List<HealthData> MemDataList = new ();
        
        // Used in pages
        public string Status { get; private set; }
        public DateTime EndTime { get; private set; }
        public int RunTime { get; private set; }
        public int RowsRead { get; private set; }
        public int RowsWritten { get; private set; }
        public int EfficiencyScore { get; private set; }
        public int AvgMemoryPercent { get; private set; }
        
        // Used in ManagerStatusHandler
        public int Cpu { get; set; }
        public static SqlConnection Connection { get; set; }
        public HealthDataHandler Health { get; set; }
        public ReconciliationDataHandler ReconciliationHandler { get; set; }
        public ErrorDataHandler ErrorHandler { get; set; }
        public string Name { get; private set; }
        public int Id { get; private set; }
        private DateTime StartTime { get; set; }
        private int ExecutionID { get; set; }

        public ManagerStatusHandler(string name, int id, DateTime startTime, int executionId)
        {
            Health = new HealthDataHandler(startTime);
            ReconciliationHandler = new ReconciliationDataHandler(startTime, ReconTriggerUpdate);
            ErrorHandler = new ErrorDataHandler(startTime, ErrorTriggerUpdate, executionId);
            Name = name;
            Id = id;
            StartTime = startTime;
            ExecutionID = executionId;
        }

        //Starts the tablestreamers and assigns the start time of the manager
        public void WatchManager()
        {
            OverviewTriggerUpdate();
            
            _healthStreamer = new SQLDependencyListener(DatabaseListenerQueryStrings.HealthSelect,
                GetSelectStringsForTableStreamer("health"), Health);
            _errorStreamer = new SQLDependencyListener(DatabaseListenerQueryStrings.ErrorSelect,
                GetSelectStringsForTableStreamer("logging"), ErrorHandler);
            _reconciliationStreamer = new SQLDependencyListener(DatabaseListenerQueryStrings.ReconciliationSelect,
                GetSelectStringsForTableStreamer("reconciliation"), ReconciliationHandler);
            
            _healthStreamer.StartListening();
            _errorStreamer.StartListening();
            _reconciliationStreamer.StartListening();
        }

        //Stops the tablestreamers, queries relevant data and calculates the EffiencyScore(TM)
        public void FinishManager()
        {
            _healthStreamer.StopListening();
            _errorStreamer.StopListening();
            _reconciliationStreamer.StopListening();
            
            AssignManagerTrackingData();
            CalculateEfficiencyScore();
            CalculateAverageMemoryUsed();
        }

        //The EfficiencyScore© algorithm is a proprietary intellectual property owned by Arthur Osnes Gottlieb.
        //Do NOT change, share or reproduce in any form.
        public void CalculateEfficiencyScore()
        {
            double averageCpu = Health.Cpu.Count > 0 ?  Health.Cpu.Average(data => data.NumericValue) : 0;
            Cpu = Convert.ToInt32(averageCpu);

            double result;
            if (RunTime == 0)
            {
                result = 0;
            }
            else
            {
                result = ((double) (RowsRead + RowsWritten) / RunTime * (averageCpu));
            }

            EfficiencyScore = Convert.ToInt32(result);
            AvgCpu = Convert.ToInt64(averageCpu);
        }
        
        //Method used to calculate the average memory usage of a manager
        public void CalculateAverageMemoryUsed()
        {
            AvgMemory = Health.Memory.Count > 0 ? Convert.ToInt64(Health.Memory.Average(data => data.NumericValue)) : MaxMemory;

            AvgMemoryPercent = Utility.CalculateMemoryUsage(AvgMemory, MaxMemory);
        }

        //Queries status, runtime, rows read and rows written from the MANAGER_TRACKING table.
        private void AssignManagerTrackingData()
        {
            using (SqlCommand command = new SqlCommand(GetManagerTrackingQueryString(), Connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        Status = (string) reader["STATUS"];
                        RunTime = (int) reader["RUNTIME"];
                        RowsRead = (int) reader["PERFORMANCECOUNTROWSREAD"];
                        RowsWritten = (int) reader["PERFORMANCECOUNTROWSWRITTEN"];
                        EndTime = (DateTime) reader["ENDTIME"];
                    }
                    reader.Close();
                }
            }
            
            //The method will retry the query up to 5 times if it fails.
            if (RunTime == 0 && _mtRetryCount < 5)
            {
                _mtRetryCount++;
                Thread.Sleep(500);
                AssignManagerTrackingData();
            }
        }

        //Returns a query string for the MANAGER_TRACKING table
        private string GetManagerTrackingQueryString()
        {
            return string.Format($"SELECT [STATUS], [RUNTIME], [PERFORMANCECOUNTROWSREAD], [PERFORMANCECOUNTROWSWRITTEN], [ENDTIME] FROM dbo.MANAGER_TRACKING WHERE MGR = '{Name}'");
        }

        //Returns the select string for the table streamers
        private string GetSelectStringsForTableStreamer(string s)
        {
            switch (s)
            {
                case "health":
                    return string.Format($"SELECT REPORT_TYPE, REPORT_NUMERIC_VALUE, LOG_TIME FROM dbo.HEALTH_REPORT " +
                                         $"WHERE (REPORT_TYPE = 'CPU' OR REPORT_TYPE = 'MEMORY')" +
                                         $"AND LOG_TIME > '{StartTime.ToString("yyyy-MM-dd HH:mm:ss.fff")}'" +
                                         "ORDER BY LOG_TIME");
                case "logging":
                    return string.Format("SELECT DISTINCT [CREATED], [LOG_MESSAGE], [LOG_LEVEL]," +
                                         "[dbo].[LOGGING_CONTEXT].[CONTEXT] " +
                                         "FROM [dbo].[LOGGING] " +
                                         "INNER JOIN [dbo].[LOGGING_CONTEXT] " +
                                         "ON (LOGGING.CONTEXT_ID = LOGGING_CONTEXT.CONTEXT_ID AND LOGGING.EXECUTION_ID = LOGGING_CONTEXT.EXECUTION_ID) " +
                                         $"WHERE CREATED > '{StartTime.ToString("yyyy-MM-dd HH:mm:ss.fff")}' " +
                                         $"AND [LOGGING_CONTEXT].[EXECUTION_ID] = '{ExecutionID}' "+
                                         "ORDER BY CREATED");
                case "reconciliation":
                    return string.Format($"SELECT [AFSTEMTDATO],[DESCRIPTION],[AFSTEMRESULTAT],[MANAGER]" +
                                         $"FROM dbo.AFSTEMNING WHERE AFSTEMTDATO > '{StartTime.ToString("yyyy-MM-dd HH:mm:ss.fff")}' " +
                                         $"ORDER BY AFSTEMTDATO");
                default:
                    throw new ArgumentException();
            }
        }
        
        //Method used to strip away the RND part found in some manager names.
        public string GetManagerNameWithoutRnd()
        {
            if (Name.Contains(','))
            {
                string[] name = Name.Split(",");
                return name[0];
            }

            return Name;
        }
    }
}