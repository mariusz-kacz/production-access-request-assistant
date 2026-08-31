using System;
using System.IO;
using System.Text;

namespace GovernedAccess.Web.Evaluation;

internal sealed class EvaluationLogFile : IDisposable
{
	private readonly object sync = new();
	private readonly StreamWriter fileWriter;
	private bool disposed;

	private EvaluationLogFile(
		StreamWriter fileWriter,
		TextWriter standardOutput,
		TextWriter standardError)
	{
		this.fileWriter = fileWriter;
		Output = new TeeTextWriter(standardOutput, fileWriter, sync);
		Error = new TeeTextWriter(standardError, fileWriter, sync);
	}

	internal TextWriter Output { get; }

	internal TextWriter Error { get; }

	internal static EvaluationLogFile Create(
		string path,
		TextWriter standardOutput,
		TextWriter standardError)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(standardOutput);
		ArgumentNullException.ThrowIfNull(standardError);

		string resolvedPath = Path.GetFullPath(path);
		string? directory = Path.GetDirectoryName(resolvedPath);
		if (directory is not null)
		{
			Directory.CreateDirectory(directory);
		}

		var stream = new FileStream(
			resolvedPath,
			FileMode.Create,
			FileAccess.Write,
			FileShare.Read);
		try
		{
			var writer = new StreamWriter(
				stream,
				new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
			{
				AutoFlush = true,
			};
			return new EvaluationLogFile(writer, standardOutput, standardError);
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		lock (sync)
		{
			if (disposed)
			{
				return;
			}

			disposed = true;
			fileWriter.Dispose();
		}
	}

	private sealed class TeeTextWriter(
		TextWriter destination,
		TextWriter copy,
		object sync) : TextWriter
	{
		public override Encoding Encoding => destination.Encoding;

		public override void Flush()
		{
			lock (sync)
			{
				destination.Flush();
				copy.Flush();
			}
		}

		public override void Write(char value)
		{
			lock (sync)
			{
				destination.Write(value);
				copy.Write(value);
			}
		}

		public override void Write(string? value)
		{
			lock (sync)
			{
				destination.Write(value);
				copy.Write(value);
			}
		}

		public override void Write(char[] buffer, int index, int count)
		{
			lock (sync)
			{
				destination.Write(buffer, index, count);
				copy.Write(buffer, index, count);
			}
		}

		public override void Write(ReadOnlySpan<char> buffer)
		{
			lock (sync)
			{
				destination.Write(buffer);
				copy.Write(buffer);
			}
		}

		public override void WriteLine()
		{
			lock (sync)
			{
				destination.WriteLine();
				copy.WriteLine();
			}
		}

		public override void WriteLine(string? value)
		{
			lock (sync)
			{
				destination.WriteLine(value);
				copy.WriteLine(value);
			}
		}

		public override void WriteLine(ReadOnlySpan<char> buffer)
		{
			lock (sync)
			{
				destination.WriteLine(buffer);
				copy.WriteLine(buffer);
			}
		}
	}
}
