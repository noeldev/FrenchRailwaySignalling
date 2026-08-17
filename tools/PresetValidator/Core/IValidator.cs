// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace PresetValidator.Core;

// Contract shared by every check. Validators are stateless and only read from
// the context, which lets the runner execute them uniformly. The method is
// asynchronous so network based checks fit the same shape as the local ones.
public interface IValidator
{
    string Name { get; }

    Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken);
}
