# Performance Logger - A Simple and Powerful Logging Tool for Unity Games

This logger only requires you to start and end the logger, the rest is handled for you.
Once activated, the logger will record the frametime for every frame; on dump, a summary will be generated, a long with pulled system specs, and the full log.

To begin, call `PerformanceLogger.StartLogger();`
To end and dump the logger, call `EndLogger(string Path, string ExtraInfo = "", bool Async = true, Action CompletionCallback = null)`
`Path` is the full file path (name included) of the dumped log file
`ExtraInfo` is a string that will be prepended to the log file, this could be useful to use as a version number
`Async` will cause the dump to run in async mode, which is highly recommended for large dumps. If this is used, you should use the `CompletionCallback`, which will execute (on the main thread) as soon as the dump process is done. This is useful for disabling a message.

If you want to log a custom event, such as spawning a boss, use `PerformanceLogger.LogCustomEvent(string EventData)`, and the event will be added to the log file.

An example of the log file can be seen here:
![alt text](https://pbs.twimg.com/media/DeMO4raXUAAlMHE.jpg:large)
