using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lab.Core.Processes
{
    public interface IProcessRunner
    {
        Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string? workingDirectory = null,
            Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null,
            bool captureOutput = true,
            CancellationToken cancellationToken = default);
    }
}
