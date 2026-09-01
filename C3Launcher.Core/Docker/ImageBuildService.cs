using System.Formats.Tar;
using C3Launcher.Core.Launching;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace C3Launcher.Core.Docker;

public sealed record BuildOutcome(bool Ok, string? Error);

public sealed class ImageBuildService
{
    /// <summary>
    /// Every file the Dockerfile COPYs. The build context is assembled from this
    /// list, so a COPY of anything missing here fails the build.
    /// </summary>
    private static readonly string[] BuildContextFiles =
    [
        "Dockerfile",
        "entrypoint.sh",
        "container-CLAUDE.md",
        "managed-settings.json",
    ];

    private readonly IDockerClient _client;

    public ImageBuildService(IDockerClient client) => _client = client;

    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Images.InspectImageAsync(image, cancellationToken);
            return true;
        }
        catch (DockerApiException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs on every launch, as cc.ps1 did: the layer cache makes an unchanged
    /// build a no-op, and it's the only way a Dockerfile edit ever reaches the image.
    /// </summary>
    public async Task<BuildOutcome> BuildAsync(
        string image,
        string? claudeVersion = null,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        if (!ContainerAssets.IsPresent)
            return new BuildOutcome(false, $"Build context is missing from {ContainerAssets.BuildContextPath}.");

        var parameters = new ImageBuildParameters
        {
            Tags = [image],
            Dockerfile = "Dockerfile",
            Remove = true,
            ForceRemove = true,
        };

        if (!string.IsNullOrWhiteSpace(claudeVersion))
        {
            parameters.BuildArgs = new Dictionary<string, string>
            {
                ["CLAUDE_VERSION"] = claudeVersion,
            };
        }

        string? failure = null;

        var progress = new CollectingProgress<JSONMessage>(message =>
        {
            if (message.Error is { } error)
            {
                failure = error.Message;
                output?.Report(error.Message ?? "build failed");
                return;
            }

            var line = message.Stream ?? message.Status;
            if (!string.IsNullOrWhiteSpace(line))
                output?.Report(line.TrimEnd('\n', '\r'));
        });

        try
        {
            await using var context = await CreateBuildContextAsync(cancellationToken);
            await _client.Images.BuildImageFromDockerfileAsync(
                parameters,
                context,
                null,
                null,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new BuildOutcome(false, ex.Message);
        }

        return failure is null ? new BuildOutcome(true, null) : new BuildOutcome(false, failure);
    }

    private static async Task<Stream> CreateBuildContextAsync(CancellationToken cancellationToken)
    {
        var tar = new MemoryStream();

        await using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var name in BuildContextFiles)
            {
                var path = Path.Combine(ContainerAssets.BuildContextPath, name);
                if (File.Exists(path))
                    await writer.WriteEntryAsync(path, name, cancellationToken);
            }
        }

        tar.Position = 0;
        return tar;
    }
}
