using System.IO.Pipes;

namespace MedCareOS.IntegrationTests.Workers;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(CheckDockerAvailable);

    public DockerFactAttribute()
    {
        if (!DockerAvailable.Value)
        {
            Skip = "Docker daemon no disponible localmente (se ejecuta en CI o con Docker Desktop activo).";
        }
    }

    private static bool CheckDockerAvailable()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            return true;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", "docker_engine", PipeDirection.InOut);
                pipe.Connect(150);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return File.Exists("/var/run/docker.sock");
    }
}
