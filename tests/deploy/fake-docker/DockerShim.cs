// docker.exe forwarder for the deploy script tests on Windows (ADR 0163): runs fake_docker.py (next to this program) with the same
// command line. Needed because System.Diagnostics.Process (Invoke-ComposeBinary in deploy\lib.ps1) only finds .exe files on PATH.
// C# 5, compiled on the test machine with the .NET Framework csc.exe:
//   csc.exe /nologo /out:docker.exe DockerShim.cs
// FAKE_DOCKER_PYTHON: the Python interpreter (default: python on PATH). Standard input is inherited; output and error are copied as bytes.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal static class DockerShim
{
    private static int Main()
    {
        string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fake_docker.py");
        string python = Environment.GetEnvironmentVariable("FAKE_DOCKER_PYTHON");
        if (string.IsNullOrEmpty(python)) { python = "python"; }

        // The rest of the raw command line is passed on unchanged, so Python parses exactly the arguments the caller encoded.
        var startInfo = new ProcessStartInfo(python, "-I \"" + script + "\" " + ArgumentsTail(Environment.CommandLine));
        startInfo.UseShellExecute = false;
        // Standard input is inherited, not redirected: a redirected StandardInput is a StreamWriter that writes the console encoding's
        // preamble (a UTF-8 BOM) as soon as .NET Framework creates it, which would corrupt the binary input under test.
        startInfo.RedirectStandardInput = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        Process process = Process.Start(startInfo);


        Stream stderr = Console.OpenStandardError();
        var error = new Thread(delegate () { Copy(process.StandardError.BaseStream, stderr); });
        error.Start();
        Copy(process.StandardOutput.BaseStream, Console.OpenStandardOutput());
        error.Join();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void Copy(Stream from, Stream to)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            to.Write(buffer, 0, read);
            to.Flush();
        }
    }

    private static string ArgumentsTail(string commandLine)
    {
        string line = commandLine.TrimStart();
        int i;
        if (line.StartsWith("\""))
        {
            i = line.IndexOf('"', 1);
            i = i < 0 ? line.Length : i + 1;
        }
        else
        {
            i = 0;
            while (i < line.Length && line[i] != ' ' && line[i] != '\t') { i++; }
        }
        return line.Substring(i).TrimStart();
    }
}
