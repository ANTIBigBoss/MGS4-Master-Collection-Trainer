using System;

namespace MGS4_Master_Collection_Trainer
{
    internal sealed class InitializationProgress
    {
        internal InitializationProgress(int completedSteps, int totalSteps, string message, bool isComplete = false)
        {
            if (totalSteps <= 0) throw new ArgumentOutOfRangeException(nameof(totalSteps));
            if (completedSteps < 0 || completedSteps > totalSteps)
                throw new ArgumentOutOfRangeException(nameof(completedSteps));
            if (isComplete && completedSteps != totalSteps)
                throw new ArgumentException("Initialization cannot finish before all steps are complete.", nameof(isComplete));

            CompletedSteps = completedSteps;
            TotalSteps = totalSteps;
            Message = message ?? string.Empty;
            IsComplete = isComplete;
        }

        internal int CompletedSteps { get; }
        internal int TotalSteps { get; }
        internal string Message { get; }
        internal bool IsComplete { get; }
    }
}
