// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Tasks;
using Amazon.ECS.Model;
using AWS.Deploy.CLI.TypeHintResponses;
using AWS.Deploy.Common;
using AWS.Deploy.Common.Recipes;
using AWS.Deploy.Orchestration.Data;

namespace AWS.Deploy.CLI.Commands.TypeHints
{
    public class ECSClusterCommand : ITypeHintCommand
    {
        private readonly IAWSResourceQueryer _awsResourceQueryer;
        private readonly IConsoleUtilities _consoleUtilities;

        public ECSClusterCommand(IAWSResourceQueryer awsResourceQueryer, IConsoleUtilities consoleUtilities)
        {
            _awsResourceQueryer = awsResourceQueryer;
            _consoleUtilities = consoleUtilities;
        }

        public async Task<object> Execute(Recommendation recommendation, OptionSettingItem optionSetting)
        {
            var clusters = await _awsResourceQueryer.ListOfECSClusters();
            var currentTypeHintResponse = recommendation.GetOptionSettingValue<ECSClusterTypeHintResponse>(optionSetting);

            var userInputConfiguration = new UserInputConfiguration<Cluster>(
                cluster => cluster.ClusterName,
                cluster => cluster.ClusterArn.Equals(currentTypeHintResponse?.ClusterArn),
                currentTypeHintResponse.NewClusterName)
            {
                AskNewName = true
            };

            var userResponse = _consoleUtilities.AskUserToChooseOrCreateNew(clusters, "Select ECS cluster to deploy to:", userInputConfiguration);

            return new ECSClusterTypeHintResponse(
                userResponse.CreateNew,
                userResponse.SelectedOption?.ClusterArn ?? string.Empty,
                userResponse.NewName ?? string.Empty);
        }
    }
}
