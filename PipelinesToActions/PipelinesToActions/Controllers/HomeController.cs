﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PipelinesToActionsWeb.Models;
using AzurePipelinesToGitHubActionsConverter.Core;

namespace PipelinesToActionsWeb.Controllers
{
    /// <summary>
    /// This controller follows the KISS principal. It's not always pretty, but HTTP server side posts was the quickest way to implement this functionality
    /// </summary>
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            ConversionResult gitHubResult = new ConversionResult();
            return View(viewName: "Index", model: gitHubResult);
        }

        [HttpPost]
        public IActionResult Index(string txtAzurePipelinesYAML)
        {
            ConversionResult gitHubResult = ProcessConversion(txtAzurePipelinesYAML);

            return View(model: gitHubResult);
        }

        private ConversionResult ProcessConversion(string input)
        {
            //process the yaml
            ConversionResult gitHubResult;
            try
            {
                Conversion conversion = new Conversion();
                gitHubResult = conversion.ConvertAzurePipelineToGitHubAction(input);
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                gitHubResult = new ConversionResult
                {
                    actionsYaml = "Error processing YAML, it's likely the original YAML is not valid" + Environment.NewLine +
                    "Original error message: " + ex.ToString(),
                    pipelinesYaml = input
                };
            }
            catch (Exception ex)
            {
                gitHubResult = new ConversionResult
                {
                    actionsYaml = "Unexpected error: " + ex.ToString(),
                    pipelinesYaml = input
                };
            }

            //Return the result
            if (gitHubResult != null)
            {
                return gitHubResult;
            }
            else
            {
                gitHubResult = new ConversionResult
                {
                    actionsYaml = "Unknown error",
                    pipelinesYaml = input
                };
                return gitHubResult;
            }
        }

        [HttpPost]
        public IActionResult CIExample()
        {
            string yaml = @"
trigger:
- master

variables:
  buildConfiguration: 'Release'
  buildPlatform: 'Any CPU'

stages:
- stage: Build
  displayName: 'Build/Test Stage'
  jobs:
  - job: Build
    displayName: 'Build job'
    pool:
      vmImage: windows-latest
    steps:

    - task: DotNetCoreCLI@2
      displayName: 'Test dotnet code projects'
      inputs:
        command: test
        projects: |
         FeatureFlags/FeatureFlags.Tests/FeatureFlags.Tests.csproj
        arguments: --configuration $(buildConfiguration) 

    - task: DotNetCoreCLI@2
      displayName: 'Publish dotnet core projects'
      inputs:
        command: publish
        publishWebProjects: false
        projects: |
         FeatureFlags/FeatureFlags.Web/FeatureFlags.Web.csproj
        arguments: --configuration $(buildConfiguration) --output $(build.artifactstagingdirectory) 
        zipAfterPublish: true

    # Publish the artifacts
    - task: PublishBuildArtifacts@1
      displayName: 'Publish Artifact'
      inputs:
        PathtoPublish: '$(build.artifactstagingdirectory)'
";
            ConversionResult gitHubResult = ProcessConversion(yaml);
            return View(viewName: "Index", model: gitHubResult);
        }

        [HttpPost]
        public IActionResult CDExample()
        {
            string yaml = @"
trigger:
- master

variables:
  buildConfiguration: 'Release'
  buildPlatform: 'Any CPU'

stages:
- stage: Deploy
  displayName: 'Deploy Prod'
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/master'))
  jobs:
  - job: Deploy
    displayName: ""Deploy job""
    pool:
      vmImage: ubuntu-latest  
    variables:
      AppSettings.Environment: 'data'
      ArmTemplateResourceGroupLocation: 'eu'
      ResourceGroupName: 'SamLearnsAzureFeatureFlags'
      WebsiteName: 'featureflags-data-eu-web'
      WebServiceName: 'featureflags-data-eu-service'
    steps:
    - task: DownloadBuildArtifacts@0
      displayName: 'Download the build artifacts'
      inputs:
        buildType: 'current'
        downloadType: 'single'
        artifactName: 'drop'
        downloadPath: '$(build.artifactstagingdirectory)'
    - task: AzureRmWebAppDeployment@3
      displayName: 'Azure App Service Deploy: web site'
      inputs:
        azureSubscription: 'SamLearnsAzure connection to Azure Portal'
        WebAppName: $(WebsiteName)
        DeployToSlotFlag: true
        ResourceGroupName: $(ResourceGroupName)
        SlotName: 'staging'
        Package: '$(build.artifactstagingdirectory)/drop/FeatureFlags.Web.zip'
        TakeAppOfflineFlag: true
        JSONFiles: '**/appsettings.json'        
    - task: AzureAppServiceManage@0
      displayName: 'Swap Slots: website'
      inputs:
        azureSubscription: 'SamLearnsAzure connection to Azure Portal'
        WebAppName: $(WebsiteName)
        ResourceGroupName: $(ResourceGroupName)
        SourceSlot: 'staging'
";
            ConversionResult gitHubResult = ProcessConversion(yaml);
            return View(viewName: "Index", model: gitHubResult);
        }

        [HttpPost]
        public IActionResult CICDExample()
        {
            string yaml = @"
trigger:
- master
pr:
  branches:
    include:
    - '*'  # must quote since ""*"" is a YAML reserved character; we want a string

variables:
  buildConfiguration: 'Release'
  buildPlatform: 'Any CPU'

stages:
- stage: Build
  displayName: 'Build/Test Stage'
  jobs:
  - job: Build
    displayName: 'Build job'
    pool:
      vmImage: windows-latest
    steps:

    - task: CopyFiles@2
      displayName: 'Copy environment ARM template files to: $(build.artifactstagingdirectory)'
      inputs:
        SourceFolder: '$(system.defaultworkingdirectory)\FeatureFlags\FeatureFlags.ARMTemplates'
        Contents: '**\*' # **\* = Copy all files and all files in sub directories
        TargetFolder: '$(build.artifactstagingdirectory)\ARMTemplates'

    - task: DotNetCoreCLI@2
      displayName: 'Test dotnet code projects'
      inputs:
        command: test
        projects: |
         FeatureFlags/FeatureFlags.Tests/FeatureFlags.Tests.csproj
        arguments: --configuration $(buildConfiguration) 

    - task: DotNetCoreCLI@2
      displayName: 'Publish dotnet core projects'
      inputs:
        command: publish
        publishWebProjects: false
        projects: |
         FeatureFlags/FeatureFlags.Web/FeatureFlags.Web.csproj
        arguments: --configuration $(buildConfiguration) --output $(build.artifactstagingdirectory) 
        zipAfterPublish: true

    - task: DotNetCoreCLI@2
      displayName: 'Publish dotnet core functional tests project'
      inputs:
        command: publish
        publishWebProjects: false
        projects: |
         FeatureFlags/FeatureFlags.FunctionalTests/FeatureFlags.FunctionalTests.csproj
        arguments: '--configuration $(buildConfiguration) --output $(build.artifactstagingdirectory)/FunctionalTests'
        zipAfterPublish: false

    - task: CopyFiles@2
      displayName: 'Copy Selenium Files to: $(build.artifactstagingdirectory)/FunctionalTests/FeatureFlags.FunctionalTests'
      inputs:
        SourceFolder: 'FeatureFlags/FeatureFlags.FunctionalTests/bin/$(buildConfiguration)/netcoreapp3.0'
        Contents: '*chromedriver.exe*'
        TargetFolder: '$(build.artifactstagingdirectory)/FunctionalTests/FeatureFlags.FunctionalTests'

    # Publish the artifacts
    - task: PublishBuildArtifacts@1
      displayName: 'Publish Artifact'
      inputs:
        PathtoPublish: '$(build.artifactstagingdirectory)'

- stage: Deploy
  displayName: 'Deploy Prod'
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/master'))
  jobs:
  - job: Deploy
    displayName: ""Deploy job""
    pool:
      vmImage: ubuntu-latest  
    variables:
      AppSettings.Environment: 'data'
      ArmTemplateResourceGroupLocation: 'eu'
      ResourceGroupName: 'SamLearnsAzureFeatureFlags'
      WebsiteName: 'featureflags-data-eu-web'
      WebServiceName: 'featureflags-data-eu-service'
    steps:
    - task: DownloadBuildArtifacts@0
      displayName: 'Download the build artifacts'
      inputs:
        buildType: 'current'
        downloadType: 'single'
        artifactName: 'drop'
        downloadPath: '$(build.artifactstagingdirectory)'
    - task: AzureResourceGroupDeployment@2
      displayName: 'Deploy ARM Template to resource group'
      inputs:
        azureSubscription: 'SamLearnsAzure connection to Azure Portal'
        resourceGroupName: $(ResourceGroupName)
        location: '[resourceGroup().location]'
        csmFile: '$(build.artifactstagingdirectory)/drop/ARMTemplates/azuredeploy.json'
        csmParametersFile: '$(build.artifactstagingdirectory)/drop/ARMTemplates/azuredeploy.parameters.json'
        overrideParameters: '-environment $(AppSettings.Environment) -locationShort $(ArmTemplateResourceGroupLocation)'
    - task: AzureRmWebAppDeployment@3
      displayName: 'Azure App Service Deploy: web site'
      inputs:
        azureSubscription: 'SamLearnsAzure connection to Azure Portal'
        WebAppName: $(WebsiteName)
        DeployToSlotFlag: true
        ResourceGroupName: $(ResourceGroupName)
        SlotName: 'staging'
        Package: '$(build.artifactstagingdirectory)/drop/FeatureFlags.Web.zip'
        TakeAppOfflineFlag: true
        JSONFiles: '**/appsettings.json'        
    - task: VSTest@2
      displayName: 'Run functional smoke tests on website and web service'
      inputs:
        searchFolder: '$(build.artifactstagingdirectory)'
        testAssemblyVer2: |
          **\FeatureFlags.FunctionalTests\FeatureFlags.FunctionalTests.dll
        uiTests: true
        runSettingsFile: '$(build.artifactstagingdirectory)/drop/FunctionalTests/FeatureFlags.FunctionalTests/test.runsettings'
    - task: AzureAppServiceManage@0
      displayName: 'Swap Slots: website'
      inputs:
        azureSubscription: 'SamLearnsAzure connection to Azure Portal'
        WebAppName: $(WebsiteName)
        ResourceGroupName: $(ResourceGroupName)
        SourceSlot: 'staging'
";
            ConversionResult gitHubResult = ProcessConversion(yaml);
            return View(viewName: "Index", model: gitHubResult);
        }

        [HttpPost]
        public IActionResult ContainerExample()
        {
            string yaml = @"
pool:
  vmImage: 'ubuntu-16.04'

strategy:
  matrix:
    DotNetCore22:
      containerImage: mcr.microsoft.com/dotnet/core/sdk:2.2
    DotNetCore22Nightly:
      containerImage: mcr.microsoft.com/dotnet/core-nightly/sdk:2.2

container: $[ variables['containerImage'] ]

resources:
  containers:
  - container: redis
    image: redis

services:
  redis: redis

variables:
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true

steps:
- task: DotNetCoreCLI@2
  displayName: Build
  inputs:
    command: build
    projects: '**/*.csproj'
    arguments: '--configuration release'

- task: DotNetCoreCLI@2
  displayName: Test
  inputs:
    command: test
    projects: '**/*Tests.csproj'
    arguments: '--configuration release'
  env:
    CONNECTIONSTRINGS_REDIS: redis:6379

- task: DotNetCoreCLI@2
  displayName: Publish
  inputs:
    command: publish
    projects: 'MyProject/MyProject.csproj'
    publishWebProjects: false
    zipAfterPublish: false
    arguments: '--configuration release'

- task: PublishPipelineArtifact@0
  displayName: Store artefact
  inputs:
    artifactName: 'MyProject'
    targetPath: 'MyProject/bin/release/netcoreapp2.2/publish/'
  condition: and(succeeded(), endsWith(variables['Agent.JobName'], 'DotNetCore22'))
";
            ConversionResult gitHubResult = ProcessConversion(yaml);
            return View(viewName: "Index", model: gitHubResult);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
