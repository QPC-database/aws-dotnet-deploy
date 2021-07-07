// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AWS.Deploy.Common;
using AWS.Deploy.Orchestration;
using AWS.Deploy.Orchestration.Utilities;
using AWS.Deploy.Recipes;

namespace AWS.Deploy.CLI.Commands
{
    /// <summary>
    /// The class supports the functionality to create a new CDK project and save it at a customer
    /// customer specified directory location.
    /// </summary>
    public class GenerateDeploymentProjectCommand
    {
        private readonly IToolInteractiveService _toolInteractiveService;
        private readonly IConsoleUtilities _consoleUtilities;
        private readonly ICdkProjectHandler _cdkProjectHandler;
        private readonly ICommandLineWrapper _commandLineWrapper;
        private readonly OrchestratorSession _session;

        public GenerateDeploymentProjectCommand(
            IToolInteractiveService toolInteractiveService,
            IConsoleUtilities consoleUtilities,
            ICdkProjectHandler cdkProjectHandler,
            ICommandLineWrapper commandLineWrapper,
            OrchestratorSession session)
        {
            _toolInteractiveService = toolInteractiveService;
            _consoleUtilities = consoleUtilities;
            _cdkProjectHandler = cdkProjectHandler;
            _commandLineWrapper = commandLineWrapper;
            _session = session;
        }

        /// <summary>
        /// This method takes a user specified directory path and generates the CDK deployment project at this location.
        /// If the provided directory path is an empty string, then a default directory is created to save the CDK deployment project.
        /// </summary>
        /// <param name="saveCdkDirectoryPath">An absolute or a relative path provided by the user.</param>
        /// <returns></returns>
        public async Task ExecuteAsync(string saveCdkDirectoryPath)
        {
            var orchestrator = GetOrchestrator();
            var recommendations = await GenerateDeploymentRecommendations(orchestrator);
            var selectedRecommendation = _consoleUtilities.AskToChooseRecommendation(recommendations);

            if (string.IsNullOrEmpty(saveCdkDirectoryPath))
                saveCdkDirectoryPath = GenerateDefaultSaveDirectoryPath(_session.ProjectDefinition.ProjectPath);

            var newDirectoryCreated = CreateSaveCdkDirectory(saveCdkDirectoryPath);

            var errorMessage = ValidateSaveCdkDirectory(saveCdkDirectoryPath);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                if (newDirectoryCreated)
                    Directory.Delete(saveCdkDirectoryPath);
                throw new InvalidSaveDirectoryForCdkProject(errorMessage);
            }

            var directoryUnderSourceControl = await IsDirectoryUnderSourceControl(saveCdkDirectoryPath);
            if (!directoryUnderSourceControl)
            {
                _toolInteractiveService.WriteLine();
                var yesNoResult = _consoleUtilities.AskYesNoQuestion("The target directory for saving the CDK " +
                    "deployment project is not being tracked by a source control system. Do you still want to continue?", YesNo.Yes);

                if (yesNoResult == YesNo.No)
                {
                    if (newDirectoryCreated)
                        Directory.Delete(saveCdkDirectoryPath);
                    return;
                }
            }

            await _cdkProjectHandler.CreateCdkProjectForDeployment(selectedRecommendation, _session, saveCdkDirectoryPath);
            var directoryInfo = GetDirectoryInfo(saveCdkDirectoryPath);

            _toolInteractiveService.WriteLine();
            _toolInteractiveService.WriteLine($"The CDK deployment project is saved at: {directoryInfo.FullName}");
        }

        /// <summary>
        /// Returns and instance of the Orchestartor class.
        /// </summary>
        /// <returns><see cref="Orchestrator"/></returns>
        private Orchestrator GetOrchestrator()
        {
            return new Orchestrator(_session, new[] { RecipeLocator.FindRecipeDefinitionsPath() });
        }

        /// <summary>
        /// This method generates the appropriate recommendations for the target deployment project.
        /// </summary>
        /// <param name="orchestrator"><see cref="Orchestrator"/></param>
        /// <returns>A list of <see cref="Recommendation"/></returns>
        private async Task<List<Recommendation>> GenerateDeploymentRecommendations(Orchestrator orchestrator)
        {
            var recommendations = await orchestrator.GenerateDeploymentRecommendations(forDeployment: false);
            if (recommendations.Count == 0)
            {
                throw new FailedToGenerateAnyRecommendations("The project you are trying to deploy is currently not supported.");
            }
            return recommendations;
        }

        /// <summary>
        /// This method takes the path to the target deployment project and creates
        /// a default save directory inside the parent folder of the current directory.
        /// For example:
        /// Target project directory - C:\Codebase\MyWebApp\xyz.csproj
        /// Generated default save directory - C:\Codebase\MyWebAppDeploymentProject If the save directory already exists, then a suffix number is attached.
        /// </summary>
        /// <param name="projectPath">The path of the target deployment project.</param>
        /// <returns>The defaukt save directory path.</returns>
        private string GenerateDefaultSaveDirectoryPath(string projectPath)
        {
            var projectDirectoryFullPath = GetDirectoryInfo(projectPath).Parent.FullName;
            var saveCdkDirectoryFullPath = projectDirectoryFullPath + "DeploymentProject";

            var suffixNumber = 0;
            while (Directory.Exists(saveCdkDirectoryFullPath))
                saveCdkDirectoryFullPath = projectDirectoryFullPath + $"DeploymentProject{++suffixNumber}";
            
            return saveCdkDirectoryFullPath;
        }

        /// <summary>
        /// This method takes a path and creates a new directory at this path if one does not already exist.
        /// </summary>
        /// <param name="path">An absolute or relative directory path.</param>
        /// <returns>A boolean to indicate in a new directory was created.</returns>
        private bool CreateSaveCdkDirectory(string path)
        {
            var newDirectoryCreated = false;
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                    newDirectoryCreated = true;
                }
                catch (Exception e)
                {
                    var errorMessage = "Failed to create a directory at the specified path.";
                    throw new InvalidSaveDirectoryForCdkProject(errorMessage, e);
                }
            }
            return newDirectoryCreated;   
        }

        /// <summary>
        /// This method takes the path to the intended location of the CDK deployment project and performs validations on it.
        /// </summary>
        /// <param name="saveCdkDirectoryPath">Relative or absolute path of the directory at which the CDK deployment project will be saved.</param>
        /// <returns>An error message or an empty string if there are no errors.</returns>
        private string ValidateSaveCdkDirectory(string saveCdkDirectoryPath)
        {
            var errorMessage = string.Empty;
            errorMessage += CheckDirectoryEmpty(saveCdkDirectoryPath) + Environment.NewLine;
            errorMessage += CheckSaveDirectoryInsideProjectDirectory(saveCdkDirectoryPath, _session.ProjectDefinition.ProjectPath);
            return errorMessage.Trim();
        }

        /// <summary>
        /// Checks if the directory is empty
        /// </summary>
        /// <param name="saveCdkDirectoryPath">Relative or absolute path of the directory at which the CDK deployment project will be saved.</param>
        /// <returns></returns>
        private string CheckDirectoryEmpty(string saveCdkDirectoryPath)
        {
            var errorMessage = string.Empty;
            if (Directory.GetFiles(saveCdkDirectoryPath).Length > 0 || Directory.GetDirectories(saveCdkDirectoryPath).Length > 0)
            {
                errorMessage = "The directory specified for saving the CDK project is non-empty. " +
                    "Please provide an empty directory path and try again.";
            }
            return errorMessage;
        }

        /// <summary>
        /// Checks if the location to save the CDK project is inside the target deployment project directory.
        /// </summary>
        /// <param name="saveCdkDirectoryPath">Relative or absolute path of the directory at which the CDK deployment project will be saved.</param>
        /// <param name="projectPath">The path of the target deployment project.</param>
        /// <returns></returns>
        private string CheckSaveDirectoryInsideProjectDirectory(string saveCdkDirectoryPath, string projectPath)
        {
            var errorMessage = string.Empty;

            var saveDirectoryFullPath = GetDirectoryInfo(saveCdkDirectoryPath).FullName;
            var projectDirectoryFullPath = GetDirectoryInfo(projectPath).Parent.FullName;
            if (saveDirectoryFullPath.Contains(projectDirectoryFullPath + Path.DirectorySeparatorChar, StringComparison.InvariantCultureIgnoreCase))
            {
                errorMessage = "The directory used to save the CDK deployment project is contained inside of " +
                    "the target deployment project directory. Please specify a different directory and try again.";
            }

            return errorMessage;
        }

        /// <summary>
        /// Checks if the location of the saved CDK deployment project is monitored by a source control system.
        /// </summary>
        /// <param name="saveCdkDirectoryPath">Relative or absolute path of the directory at which the CDK deployment project will be saved.</param>
        /// <returns></returns>
        private async Task<bool> IsDirectoryUnderSourceControl(string saveCdkDirectoryPath)
        {
            var gitStatusResult = await _commandLineWrapper.TryRunWithResult("git status", saveCdkDirectoryPath);
            var svnStatusResult = await _commandLineWrapper.TryRunWithResult("svn status", saveCdkDirectoryPath);
            return gitStatusResult.Success || svnStatusResult.Success;
        }

        /// <summary>
        /// Returns an instance of DirectoryInfo based on the specified path.
        /// </summary>
        /// <param name="path">A relative or absolute string path.</param>
        /// <returns><see cref="DirectoryInfo"/></returns>
        private DirectoryInfo GetDirectoryInfo(string path)
        {
            try
            {
                return new DirectoryInfo(path);
            }
            catch (Exception ex)
            {
                throw new FailedToFindDirectoryInfo($"Failed to find directory info for the path - {path}", ex);
            }
        }

    }
}
