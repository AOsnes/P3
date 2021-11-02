using System.Dynamic;

namespace BlazorApp.DataStreaming
{
    public static class SqlQueryStrings
    {
        public const string HealthSelect = "SELECT REPORT_TYPE, REPORT_NUMERIC_VALUE, LOG_TIME FROM dbo.HEALTH_REPORT WHERE MONITOR_NO = 8 AND REPORT_TYPE = 'CPU' OR REPORT_TYPE = 'MEMORY' ORDER BY LOG_TIME";
        public const string ErrorSelect = "SELECT [CREATED], [LOG_MESSAGE], [LOG_LEVEL], [dbo].[LOGGING].[CONTEXT_ID], [dbo].[LOGGING_CONTEXT].[CONTEXT] FROM [dbo].[LOGGING] INNER JOIN [dbo].[LOGGING_CONTEXT] ON (LOGGING.CONTEXT_ID = LOGGING_CONTEXT.CONTEXT_ID) ORDER BY CREATED";
        public const string ReconciliationSelect = "SELECT [AFSTEMTDATO],[DESCRIPTION],[MANAGER],[AFSTEMRESULTAT] FROM [dbo].[AFSTEMNING] WHERE AFSTEMTDATO ORDER BY AFSTEMTDATO";
    }
}