using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;
using Moq;
using NUnit.Framework;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Repository.Hierarchy;

namespace Logging
{
    [TestFixture]
    public class AsyncRolloverAppenderTest
    {
        [Test]
        public void WhenNotConfigured_NoEventsAreBuffered()
        {
            var roAppender = new AsyncRolloverAppender();
            roAppender.ActivateOptions();

            for (int i = 0; i < 100; i++)
            {
                roAppender.DoAppend(new LoggingEvent(new LoggingEventData {Message = "test" + i}));
            }

            Thread.Sleep(250);
            roAppender.Close();
            Assert.That(roAppender.BufferCount, Is.EqualTo(0));            
        }

        [Test]
        public void WhenBufferIsFull_EventsAreDiscarded()
        {
            var roAppender = new AsyncRolloverAppender();
            roAppender.MaxBufferCount = 10;
            var appender = new Mock<IAppender>();
            appender.Setup(a => a.DoAppend(It.IsAny<LoggingEvent>())).Callback(() => Thread.Sleep(1000));
            roAppender.AddAppender(appender.Object);
            roAppender.ActivateOptions();

            for (int i = 0; i < 20; i++)
            {
                roAppender.DoAppend(new LoggingEvent(new LoggingEventData { Message = "test" + i }));
            }

            roAppender.Close();
            Assert.That(roAppender.BufferCount, Is.LessThan(10 + 2));
        }

        [Test]
        public void WhenConfigured_EventsAreSent()
        {
            var events = new List<LoggingEvent>();
            var roAppender = new AsyncRolloverAppender();
            var appender = new Mock<IAppender>();
            appender.Setup(a => a.DoAppend(It.IsAny<LoggingEvent>())).Callback((LoggingEvent e) => events.Add(e));
            roAppender.AddAppender(appender.Object);
            roAppender.ActivateOptions();

            roAppender.DoAppend(new LoggingEvent(new LoggingEventData { Message = "test1"}));

            Thread.Sleep(250);
            roAppender.Close();
            Assert.That(events.Count, Is.EqualTo(1));
        }

        [Test]
        public void WhenConfigured_EventsAreRolledOver()
        {
            var events = new List<LoggingEvent>();
            var roAppender = new AsyncRolloverAppender();
            var errorAppender = new Mock<IAppender>();
            errorAppender.Setup(ea => ea.DoAppend(It.IsAny<LoggingEvent>())).Throws(new Exception("error"));
            var appender = new Mock<IAppender>();
            appender.Setup(a => a.DoAppend(It.IsAny<LoggingEvent>())).Callback((LoggingEvent e) => events.Add(e));
            roAppender.AddAppender(errorAppender.Object);   // First appender, will error
            roAppender.AddAppender(appender.Object);        // Next appender should be roll to on error
            roAppender.ActivateOptions();

            roAppender.DoAppend(new LoggingEvent(new LoggingEventData { Message = "test1" }));

            Thread.Sleep(250);
            roAppender.Close();
            Assert.That(events.Count, Is.EqualTo(1));
        }

        [Test]
        public void WhenConfiguredWithAppenderThatSwallowsErrors_EventsAreRolledOver()
        {
            var events = new List<LoggingEvent>();
            var roAppender = new AsyncRolloverAppender();
            var fa = new FileAppender();
            fa.File = @"Z:\NonExistantDirectory\data.dat";
            var appender = new Mock<IAppender>();
            appender.Setup(a => a.DoAppend(It.IsAny<LoggingEvent>())).Callback((LoggingEvent e) => events.Add(e));
            roAppender.AddAppender(fa);                     // First appender, will error
            roAppender.AddAppender(appender.Object);        // Next appender should be roll to on error
            roAppender.ActivateOptions();

            roAppender.DoAppend(new LoggingEvent(new LoggingEventData { Message = "test1" }));

            Thread.Sleep(250);
            roAppender.Close();
            Assert.That(events.Count, Is.EqualTo(1));
        }

        [Test]
        public void WhenConfigured_RolloverLoggerIsNotified()
        {
            BasicConfigurator.Configure();
            // Configure the logger and appender for rollover notification
            var events = new List<LoggingEvent>();
            string notifyLoggerName = "NotifyLogger";
            var notifyLogger = (Logger)LogManager.GetLogger(notifyLoggerName).Logger;
            var notifyAppender = new Mock<IAppender>();
            notifyAppender.Setup(ea => ea.DoAppend(It.IsAny<LoggingEvent>())).Callback((LoggingEvent e) => events.Add(e));
            notifyLogger.AddAppender(notifyAppender.Object);
            
            var roAppender = new AsyncRolloverAppender();
            var errorAppender = new Mock<IAppender>();
            errorAppender.Setup(ea => ea.DoAppend(It.IsAny<LoggingEvent>())).Throws(new Exception("error"));
            var appender = new Mock<IAppender>();
            appender.Setup(a => a.DoAppend(It.IsAny<LoggingEvent>()));
            roAppender.RolloverNotificationLoggerName = notifyLoggerName;
            roAppender.AddAppender(errorAppender.Object);   // First appender, will error
            roAppender.AddAppender(appender.Object);        // Next appender should be roll to on error
            roAppender.ActivateOptions();

            
            roAppender.DoAppend(new LoggingEvent(new LoggingEventData { Message = "test1" }));

            Thread.Sleep(250);
            roAppender.Close();

            Assert.That(events.Count, Is.EqualTo(1));
            StringAssert.Contains("rolling to the next appender", events[0].MessageObject.ToString());
            appender.Verify();
        }

        [Test]
        public void WhenConfigured_ResetOccurs()
        {
            var events = new List<LoggingEvent>();
            int count = 0;
            var roAppender = new AsyncRolloverAppender();
            var errorAppender = new Mock<IAppender>();
            errorAppender.Setup(ea => ea.DoAppend(It.IsAny<LoggingEvent>())).Callback((LoggingEvent e) =>
                    {
                        count++;
                        if (count == 1)
                        {
                            throw new Exception("error");
                        }
                        events.Add(e);
                    });
            var appender = new Mock<IAppender>();
            appender.Setup(a => a.DoAppend(It.IsAny<LoggingEvent>()));
            roAppender.ResetRolloverCheck = 1;
            roAppender.AddAppender(errorAppender.Object);   // First appender, will error
            roAppender.AddAppender(appender.Object);        // Next appender should be roll to on error
            roAppender.ActivateOptions();


            // Should error roll to next appender
            roAppender.DoAppend(new LoggingEvent(new LoggingEventData { Message = "test1" }));
            // wait out the reset value
            Thread.Sleep(1200); 
            // Should go to the first appender
            roAppender.DoAppend(new LoggingEvent(new LoggingEventData { Message = "test2" }));

            Thread.Sleep(250);
            roAppender.Close();

            Assert.That(events.Count, Is.EqualTo(1));
            StringAssert.Contains("test2", events[0].RenderedMessage);
        }

        [Test]
        public void FromXmlConfiguration()
        {
            string testFile = Path.Combine(Environment.CurrentDirectory, "AsyncRolloverAppenderTest.log");
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
            string config = @"
<log4net>
    <appender name='fileError' type='log4net.Appender.FileAppender'>
      <param name='File' value='Z:\NonExistantDirectory\data.dat'/>
      <layout type='log4net.Layout.PatternLayout'>
        <param name='ConversionPattern' value='%d [%t] %-5p  %m%n'/>
      </layout>
    </appender>

    <appender name='fileOK' type='log4net.Appender.FileAppender'>
      <param name='File' value='" + testFile  + @"'/>
      <layout type='log4net.Layout.PatternLayout'>
        <param name='ConversionPattern' value='%d [%t] %-5p  %m%n'/>
      </layout>
    </appender>

    <appender name='rollover' type='" + typeof(AsyncRolloverAppender).AssemblyQualifiedName + @"'>
        <appender-ref ref='fileError'/>
        <appender-ref ref='fileOK'/>      
    </appender>

    <root>
      <level value='DEBUG'/>
      <appender-ref ref='rollover' />
    </root>

  </log4net>
";
            var doc = new XmlDocument();
            doc.LoadXml(config);
            log4net.Config.XmlConfigurator.Configure(doc.DocumentElement);

            var logger = LogManager.GetLogger("test");
            logger.Debug("Test Message");
            
            LogManager.Shutdown();
            Thread.Sleep(250);
            var data = File.ReadAllText(testFile);
            Assert.That(data, Is.Not.Null);
            StringAssert.Contains("Test Message", data);
        }
    }
}
