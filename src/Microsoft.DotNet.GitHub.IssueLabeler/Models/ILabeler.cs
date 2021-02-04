﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Hubbup.MikLabelModel;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    public interface ILabeler
    {
        Task<LabelSuggestion> PredictUsingModelsFromStorageQueue(string owner, string repo, int number);
        Task AddUntriagedIssuesToProjectBoard(string owner, string repo);
        Task MoveFromUncommittedToFuture(string owner, string repo);
        Task AddToPrColumn(string owner, string repo);
        Task AddMissingTriagedFuture(string owner, string repo);
        Task AddMissingTriaged6_0(string owner, string repo);
    }
}