using Microsoft.VisualStudio.Telemetry;
using Microsoft.VisualStudio.Text.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vim.EditorHost.Implementation.Misc
{
#if VS_SPECIFIC_2022
#elif VS_SPECIFIC_2017 || VS_SPECIFIC_2019
    [Export(typeof(ILoggingServiceInternal))]
    internal sealed class BasicLoggingServiceInternal : ILoggingServiceInternal
    {
#if VS_SPECIFIC_2017

        void ILoggingServiceInternal.AdjustCounter(string key, string name, int delta)
        {
        }

        void ILoggingServiceInternal.PostCounters()
        {
        }

        void ILoggingServiceInternal.PostEvent(string key, params object[] namesAndProperties)
        {
        }

        void ILoggingServiceInternal.PostEvent(string key, IReadOnlyList<object> namesAndProperties)
        {
        }

        void ILoggingServiceInternal.PostEvent(DataModelEventType eventType, string eventName, TelemetryResult result, params (string name, object property)[] namesAndProperties)
        {
        }

        void ILoggingServiceInternal.PostEvent(DataModelEventType eventType, string eventName, TelemetryResult result, IReadOnlyList<(string name, object property)> namesAndProperties)
        {
        }

        void ILoggingServiceInternal.PostFault(string eventName, string description, Exception exceptionObject, string additionalErrorInfo, bool? isIncludedInWatsonSample)
        {
        }

#elif VS_SPECIFIC_2019

        void ILoggingServiceInternal.AdjustCounter(string key, string name, int delta)
        {
        }

        object ILoggingServiceInternal.CreateTelemetryOperationEventScope(string eventName, Microsoft.VisualStudio.Text.Utilities.TelemetrySeverity severity, object[] correlations, IDictionary<string, object> startingProperties)
        {
            return null;
        }

        void ILoggingServiceInternal.EndTelemetryScope(object telemetryScope, Microsoft.VisualStudio.Text.Utilities.TelemetryResult result, string summary)
        {
        }

        object ILoggingServiceInternal.GetCorrelationFromTelemetryScope(object telemetryScope)
        {
            return null;
        }

        void ILoggingServiceInternal.PostCounters()
        {
        }

        void ILoggingServiceInternal.PostEvent(string key, params object[] namesAndProperties)
        {
        }

        void ILoggingServiceInternal.PostEvent(string key, IReadOnlyList<object> namesAndProperties)
        {
        }

        void ILoggingServiceInternal.PostEvent(TelemetryEventType eventType, string eventName, Microsoft.VisualStudio.Text.Utilities.TelemetryResult result, params (string name, object property)[] namesAndProperties)
        {
        }

        void ILoggingServiceInternal.PostEvent(TelemetryEventType eventType, string eventName, Microsoft.VisualStudio.Text.Utilities.TelemetryResult result, IReadOnlyList<(string name, object property)> namesAndProperties)
        {
        }

        void ILoggingServiceInternal.PostFault(string eventName, string description, Exception exceptionObject, string additionalErrorInfo, bool? isIncludedInWatsonSample, object[] correlations)
        {
        }
#else
#error Unsupported configuration
#endif
    }
#else
#error Unsupported configuration
#endif
}