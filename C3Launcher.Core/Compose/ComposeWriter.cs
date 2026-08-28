using System.Text;

namespace C3Launcher.Core.Compose;

/// <summary>
/// Emits the compose file the container is launched from. Compose rather than
/// docker run flags because .mountignore can produce enough bind mounts to blow
/// past the Windows 32,767-character command-line limit.
/// </summary>
public static class ComposeWriter
{
    public static string Write(ComposeSpec spec)
    {
        var yaml = new StringBuilder();

        yaml.AppendLine("services:");
        yaml.AppendLine($"  {spec.ServiceName}:");
        yaml.AppendLine($"    image: {Quote(spec.Image)}");
        yaml.AppendLine($"    stdin_open: {Bool(spec.StdinOpen)}");
        yaml.AppendLine($"    tty: {Bool(spec.Tty)}");
        yaml.AppendLine($"    read_only: {Bool(spec.ReadOnlyRootFilesystem)}");

        if (spec.Labels.Count > 0)
        {
            yaml.AppendLine("    labels:");
            foreach (var (key, value) in spec.Labels)
                yaml.AppendLine($"      {Quote(key)}: {Quote(value)}");
        }

        AppendScalarList(yaml, "cap_drop", spec.CapDrop);
        AppendScalarList(yaml, "security_opt", spec.SecurityOpt);
        AppendScalarList(yaml, "tmpfs", spec.Tmpfs);

        if (spec.Volumes.Count > 0)
        {
            yaml.AppendLine("    volumes:");
            foreach (var volume in spec.Volumes)
            {
                yaml.AppendLine("      - type: bind");
                yaml.AppendLine($"        source: {Quote(volume.Source)}");
                yaml.AppendLine($"        target: {Quote(volume.Target)}");
                if (volume.ReadOnly)
                    yaml.AppendLine("        read_only: true");
            }
        }

        AppendScalarList(yaml, "command", spec.Command);

        return yaml.ToString();
    }

    public static void WriteTo(ComposeSpec spec, string path) =>
        File.WriteAllText(path, Write(spec), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static void AppendScalarList(StringBuilder yaml, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return;

        yaml.AppendLine($"    {key}:");
        foreach (var value in values)
            yaml.AppendLine($"      - {Quote(value)}");
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";
}
