using WendYourWay.Cli;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Ctrl+C stops the command cleanly: Npgsql asks PostgreSQL to cancel what it's running, so no
// build or import is left working (and holding locks) after wend has gone.
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
// A build or import can take hours: Windows mustn't sleep under it (T035).
using var awake = KeepAwake.WhileRunning();
return await CommandLine.Run(args, Console.Out, stop.Token);
