using ManyConsole;
using System;
using System.Diagnostics;

namespace TPLDataFlow
{
    class Program
    {
        static int Main(string[] args)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var commands = ConsoleCommandDispatcher.FindCommandsInSameAssemblyAs(typeof(Program));
                foreach (var c in commands)
                {
                    c.SkipsCommandSummaryBeforeRunning();
                }
                return ConsoleCommandDispatcher.DispatchCommand(commands, args, Console.Out);
            }
            catch (Exception exc)
            {
                Console.Error.WriteLine(exc);
                return 1;
            }
            finally
            {
                Console.WriteLine("Elapsed: " + sw.Elapsed);
            }
        }
    }
}
