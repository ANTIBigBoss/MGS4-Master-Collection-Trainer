using System;
using System.Linq;

namespace MGS4_Master_Collection_Trainer
{
    /// <summary>Counts actual readiness checkpoints; retries never advance progress by elapsed time.</summary>
    internal sealed class InitializationManager
    {
        internal const int TotalSteps = 10;
        private static readonly string[] WaitingMessages =
        {
            "Waiting for MGS4. Please start mgs4.exe.",
            "Checking existing trainer settings...",
            "Preparing player data...",
            "Preparing inventory data...",
            "Preparing stage controls...",
            "Preparing graphics controls...",
            "Waiting for the stage list. Load into gameplay.",
            "Waiting for player stats. Load into gameplay.",
            "Waiting for inventory values. Open the in-game item menu.",
            "Checking which cheats are available..."
        };
        private readonly bool[] ready = new bool[TotalSteps];
        private readonly string[] problems = new string[TotalSteps];
        private readonly IProgress<InitializationProgress> sink;

        internal InitializationManager(IProgress<InitializationProgress> sink, InitializationManager previous = null)
        {
            this.sink = sink;
            if (previous == null) return;
            Array.Copy(previous.ready, ready, TotalSteps);
            Array.Copy(previous.problems, problems, TotalSteps);
        }

        internal void SetStep(int index, bool isReady, string message = null)
        {
            if (index < 0 || index >= TotalSteps) throw new ArgumentOutOfRangeException(nameof(index));
            ready[index] = isReady;
            problems[index] = isReady ? null : message;
            sink?.Report(Build());
        }

        internal void Report(string message)
        {
            int completed = ready.Count(value => value);
            sink?.Report(new InitializationProgress(completed, TotalSteps, message, completed == TotalSteps));
        }

        internal InitializationProgress Build()
        {
            int completed = ready.Count(value => value);
            int pending = Array.FindIndex(ready, value => !value);
            return new InitializationProgress(completed, TotalSteps,
                pending < 0 ? "Initialization complete. The trainer is ready."
                    : problems[pending] ?? WaitingMessages[pending], completed == TotalSteps);
        }
    }
}
