﻿using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Kiota.Builder.Configuration;
using Kiota.Builder.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Services;
using Microsoft.Plugins.Manifest;
using Moq;
using Xunit;

namespace Kiota.Builder.Tests.Plugins;
public sealed class PluginsGenerationServiceTests : IDisposable
{
    private readonly HttpClient _httpClient = new();
    [Fact]
    public void Defensive()
    {
        Assert.Throws<ArgumentNullException>(() => new PluginsGenerationService(null, OpenApiUrlTreeNode.Create(), new(), "foo"));
        Assert.Throws<ArgumentNullException>(() => new PluginsGenerationService(new(), null, new(), "foo"));
        Assert.Throws<ArgumentNullException>(() => new PluginsGenerationService(new(), OpenApiUrlTreeNode.Create(), null, "foo"));
        Assert.Throws<ArgumentException>(() => new PluginsGenerationService(new(), OpenApiUrlTreeNode.Create(), new(), string.Empty));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    [Fact]
    public async Task GeneratesManifest()
    {
        var simpleDescriptionContent = @"openapi: 3.0.0
info:
  title: test
  version: 1.0
servers:
  - url: http://localhost/
    description: There's no place like home
paths:
  /test:
    get:
      operationId: test
      responses:
        '200':
          description: test";
        var workingDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var simpleDescriptionPath = Path.Combine(workingDirectory) + "description.yaml";
        await File.WriteAllTextAsync(simpleDescriptionPath, simpleDescriptionContent);
        var mockLogger = new Mock<ILogger<PluginsGenerationService>>();
        var openAPIDocumentDS = new OpenApiDocumentDownloadService(_httpClient, mockLogger.Object);
        var outputDirectory = Path.Combine(workingDirectory, "output");
        var generationConfiguration = new GenerationConfiguration
        {
            OutputPath = outputDirectory,
            OpenAPIFilePath = "openapiPath",
            PluginTypes = [PluginType.Microsoft, PluginType.APIManifest],
            ClientClassName = "client",
            ApiRootUrl = "http://localhost/", //Kiota builder would set this for us
        };
        var (openAPIDocumentStream, _) = await openAPIDocumentDS.LoadStreamAsync(simpleDescriptionPath, generationConfiguration, null, false);
        var openApiDocument = await openAPIDocumentDS.GetDocumentFromStreamAsync(openAPIDocumentStream, generationConfiguration);
        var urlTreeNode = OpenApiUrlTreeNode.Create(openApiDocument, Constants.DefaultOpenApiLabel);

        var pluginsGenerationService = new PluginsGenerationService(openApiDocument, urlTreeNode, generationConfiguration, workingDirectory);
        await pluginsGenerationService.GenerateManifestAsync();

        Assert.True(File.Exists(Path.Combine(outputDirectory, ManifestFileName)));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "client-apimanifest.json")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, OpenApiFileName)));
        var manifestContent = await File.ReadAllTextAsync(Path.Combine(outputDirectory, ManifestFileName));
        using var jsonDocument = JsonDocument.Parse(manifestContent);
        var resultingManifest = PluginManifestDocument.Load(jsonDocument.RootElement);
        Assert.Equal(OpenApiFileName, resultingManifest.Document.Runtimes.OfType<OpenApiRuntime>().First().Spec.Url);
    }
    private const string ManifestFileName = "client-microsoft.json";
    private const string OpenApiFileName = "client-openapi.yml";
}
