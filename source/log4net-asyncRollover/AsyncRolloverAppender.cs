using System;
using System.Collections.Concurrent;
using System.Threading;
using log4net;
using log4net.Appender;
using log4net.Core;

namespace Logging
{
    /// <summary>
    /// log4net appender that buffers log evens and asynchronously logs to a configured appender.
    /// If the configured appender fails with an error, the next appender will be used.
    /// Features
    ///     * Configurable buffer to restrict memory growth
    ///     * Reset after a certain period of time to the first (main) appender
    ///     * Configure logger name for notification on rollover (such as an email) 
    ///         using standard log4net configuration
    /// </summary>
    public class AsyncRolloverAppender : ForwardingAppender
    {
        private readonly AutoResetEvent _notify;
        private Thread _appenderThread;
        private readonly ConcurrentQueue<LoggingEvent> _logEvents;
        private bool _closing;
        private DateTime _lastRollloverOccured;
        private AppenderSkeleton _appenderError;

        public AsyncRolloverAppender()
        {
            _notify = new AutoResetEvent(false);
            _logEvents = new ConcurrentQueue<LoggingEvent>();
            MaxBufferCount = 10000;
            ResetRolloverCheck = 0;
            _closing = false;
        }

        /// <summary>
        /// Maximum number of items the buffer wil hold before descarding log events.
        /// Used to cap memory consumption when an appender is timed out waiting for a connection
        /// or some other long operations.
        /// </summary>
        public int MaxBufferCount { get; set; }

        /// <summary>
        /// When set, this logger will get a FATAL error message when an appender fails
        /// and the next appender is used.
        /// </summary>
        public string RolloverNotificationLoggerName { get; set; }

        /// <summary>
        /// If greater than zero, number of seconds before resetting to the first appender.
        /// This allows scenarios where the appender is writing to a database or network and fails,
        /// and a rollover occurs. After this amount of time this appender will reset to the first
        /// appender giving it time for the resource to come back online.
        /// </summary>
        public int ResetRolloverCheck { get; set; }

        public int BufferCount { get { return _logEvents.Count; } }

        public override void ActivateOptions()
        {
            base.ActivateOptions();
            SetupErrorHandlers();
            _appenderThread = new Thread(AppendThreadWork) { IsBackground = true };
            _appenderThread.Start();
        }

        private void SetupErrorHandlers()
        {
            foreach (var appender in Appenders)
            {
                var ap = appender as AppenderSkeleton;
                if (ap != null)
                {
                    ap.ErrorHandler = new RolloverErrorHandler(this, ap);
                }
            }
        }

        protected override void OnClose()
        {
            base.OnClose();
            _closing = true;
            _notify.Set();
            _appenderThread.Join(250);
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (HasAppenders() && _logEvents.Count <= MaxBufferCount)
            {
                loggingEvent.Fix = FixFlags.All;
                _logEvents.Enqueue(loggingEvent);
                _notify.Set();
            }
        }

        private bool HasAppenders()
        {
            return Appenders.Count > 0;
        }

        private void AppendThreadWork()
        {
            var appenderIndex = 0;
            while (!_closing)
            {
                _notify.WaitOne(TimeSpan.FromSeconds(2));
                if (!_closing)
                {
                    while (!_logEvents.IsEmpty)
                    {
                        LoggingEvent levent;
                        if (_logEvents.TryDequeue(out levent))
                        {
                            appenderIndex = CheckForRolloverReset(appenderIndex);
                            while (levent != null && appenderIndex < Appenders.Count)
                            {
                                try
                                {
                                    Appenders[appenderIndex].DoAppend(levent);
                                    if (AppenderError())
                                    {
                                        appenderIndex = NextAppender(appenderIndex);
                                    }
                                    else
                                    {
                                        levent = null;
                                    }
                                }
                                catch (Exception e)
                                {
                                    appenderIndex = NextAppender(appenderIndex, e);
                                }
                            }
                        }
                    }
                    _notify.Reset();
                }
            }
        }

        private int CheckForRolloverReset(int appenderIndex)
        {
            int result = appenderIndex;
            if (ResetRolloverCheck > 0 && (DateTime.Now - _lastRollloverOccured).TotalSeconds > ResetRolloverCheck)
            {
                result = 0;
            }
            return result;
        }

        private int NextAppender(int appenderIndex, Exception ex = null)
        {
            NotifyOfChangedAppender(appenderIndex, ex);
            _lastRollloverOccured = DateTime.Now;
            var result = appenderIndex + 1;
            return result;
        }

        private void NotifyOfChangedAppender(int failedAppenderIndex, Exception ex = null)
        {
            if (!String.IsNullOrWhiteSpace(RolloverNotificationLoggerName))
            {
                var appender = Appenders[failedAppenderIndex];
                var appenderName = appender == null ? "unknown" : appender.GetType().Name;
                var logger = LogManager.GetLogger(RolloverNotificationLoggerName);
                logger.Fatal(String.Format("Appender {0} has failed, rolling to the next appender", appenderName), ex);
            }
        }

        private bool AppenderError()
        {
            bool result = false;
            if (_appenderError != null)
            {
                result = true;
                _appenderError = null;
            }
            return result;
        }

        internal void AppenderError(AppenderSkeleton appender)
        {
            _appenderError = appender;
        }
    }

    /// <summary>
    /// Implementation of IErrorHandler to attach to appenders to detect errors since most
    /// appenders (all that inherit from AppenderSkeletion) will swallow all exceptions and 
    /// just notify this interface.
    /// </summary>
    internal class RolloverErrorHandler : IErrorHandler
    {
        private readonly AsyncRolloverAppender _rolloverAppender;
        private readonly AppenderSkeleton _appender;
        private bool _errorOccured;

        public RolloverErrorHandler(AsyncRolloverAppender rolloverAppender, AppenderSkeleton appender)
        {
            _rolloverAppender = rolloverAppender;
            _appender = appender;
        }

        public void Error(string message, Exception e, ErrorCode errorCode)
        {
            Notify();
        }

        public void Error(string message, Exception e)
        {
            Notify();
        }

        public void Error(string message)
        {
            Notify();
        }

        private void Notify()
        {
            if (!_errorOccured)
            {
                _rolloverAppender.AppenderError(_appender);
                _errorOccured = true;
            }
        }
    }
}
