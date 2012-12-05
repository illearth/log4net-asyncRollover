## Overview
Log4net appender that buffers incoming log events and asychronously writes events to the first configured appender. If the current appender fails, the appender will roll over to the next defined appender. This is useful when the main appender is writing to a resource that may go down or timeout, such as a network or database, and you don't want your application to block or lose logs.

### Features
* **Configurable maximum buffer size** - Limit the number of events that are stored before they are discarded. Prevents high consumption of memory while waiting to log to an appender, such as a two minute network timeout.
* **Rollover notification** - An appender, such as an SmtpAppender, can be configured to be notified when an appender fails and rollover happens.
* **Automatic Reset** - A time period can be configured when the appender will reset and try the first appender again. This allows the appender to attempt to switch back to the first appender to see if the resource came back online.

### License
Distributed under the [MIT License](http://opensource.org/licenses/mit-license.php)