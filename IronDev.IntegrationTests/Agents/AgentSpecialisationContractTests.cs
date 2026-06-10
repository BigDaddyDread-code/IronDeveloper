using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class AgentSpecialisationContractTests
{
    private static readonly AgentSpecialisationValidator Validator = new();

    [TestMethod]
    public void AgentSpecialisationContracts_ExposeExpectedCoreTypes()
    {
        Assert.IsNotNull(typeof(AgentSpecialisationDefinition));
        Assert.IsNotNull(typeof(AgentSpecialisationKind));
        Assert.IsNotNull(typeof(AgentSpecialisationInputRequirement));
        Assert.IsNotNull(typeof(AgentSpecialisationOutputRequirement));
        Assert.IsNotNull(typeof(AgentSpecialisationEvidenceRequirement));
        Assert.IsNotNull(typeof(AgentSpecialisationValidationRequirement));
        Assert.IsNotNull(typeof(AgentSpecialisationForbiddenBehaviour));
        Assert.IsNotNull(typeof(AgentSpecialisationAuthorityBoundary));
        Assert.IsNotNull(typeof(AgentSpecialisationCompatibilityResult));
        Assert.IsNotNull(typeof(AgentSpecialisationValidator));
    }

    [TestMethod]
    public void AgentSpecialisation_ValidReviewSpecialisation_HasNoValidationErrors()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation());

        AssertNoErrors(issues);
    }

    [TestMethod]
    public void AgentSpecialisation_ValidMemoryImprovementSpecialisation_HasNoValidationErrors()
    {
        var issues = Validator.Validate(BuildMemoryImprovementSpecialisation());

        AssertNoErrors(issues);
    }

    [TestMethod]
    public void AgentSpecialisation_ReviewSpecialisation_IsCompatibleWithIndependentCriticDefinition()
    {
        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            BuildReviewSpecialisation());

        Assert.IsTrue(result.IsCompatible, FormatIssues(result.Issues));
        AssertNoErrors(result.Issues);
    }

    [TestMethod]
    public void AgentSpecialisation_MemoryImprovementSpecialisation_IsCompatibleWithMemoryImprovementDefinition()
    {
        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.MemoryImprovementAgent,
            BuildMemoryImprovementSpecialisation());

        Assert.IsTrue(result.IsCompatible, FormatIssues(result.Issues));
        AssertNoErrors(result.Issues);
    }

    [TestMethod]
    public void AgentSpecialisation_AuthorityBoundaryCannotGrantPower()
    {
        var invalidBoundaries = new[]
        {
            AgentSpecialisationAuthorityBoundary.None with { CanGrantApproval = true },
            AgentSpecialisationAuthorityBoundary.None with { CanRepresentHumanDecision = true },
            AgentSpecialisationAuthorityBoundary.None with { CanOverridePolicy = true },
            AgentSpecialisationAuthorityBoundary.None with { CanExecuteTools = true },
            AgentSpecialisationAuthorityBoundary.None with { CanMutateSource = true },
            AgentSpecialisationAuthorityBoundary.None with { CanCallExternalSystems = true },
            AgentSpecialisationAuthorityBoundary.None with { CanPromoteMemory = true },
            AgentSpecialisationAuthorityBoundary.None with { CanCreateAuthority = true },
            AgentSpecialisationAuthorityBoundary.None with { CanCreateRuntimeAction = true },
            AgentSpecialisationAuthorityBoundary.None with { CanWriteMemory = true }
        };

        foreach (var boundary in invalidBoundaries)
        {
            var issues = Validator.Validate(BuildReviewSpecialisation() with
            {
                AuthorityBoundary = boundary
            });

            AssertHasIssue(issues, AgentSpecialisationValidator.AuthorityBoundaryCannotGrantPower);
        }
    }

    [TestMethod]
    public void AgentSpecialisation_DangerousCapabilitiesCannotBeRequired()
    {
        var dangerousCapabilities = new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.CallExternalSystem,
            AgentCapability.PromoteCollectiveMemory,
            AgentCapability.RepresentHumanApproval,
            AgentCapability.RepresentHumanPromotionDecision,
            AgentCapability.BlockExecution
        };

        foreach (var capability in dangerousCapabilities)
        {
            var definition = BuildReviewSpecialisation() with
            {
                RequiredCapabilities = BuildReviewSpecialisation().RequiredCapabilities
                    .Concat(new[] { capability })
                    .ToArray()
            };

            var issues = Validator.Validate(definition);

            AssertHasIssue(issues, AgentSpecialisationValidator.DangerousCapabilityRequired);
        }
    }

    [TestMethod]
    public void AgentSpecialisation_DangerousCapabilitiesMustBeForbidden()
    {
        var requiredForbiddenCapabilities = new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.CallExternalSystem,
            AgentCapability.PromoteCollectiveMemory,
            AgentCapability.RepresentHumanApproval,
            AgentCapability.RepresentHumanPromotionDecision,
            AgentCapability.BlockExecution
        };

        foreach (var capability in requiredForbiddenCapabilities)
        {
            var definition = BuildReviewSpecialisation() with
            {
                ForbiddenCapabilities = BuildReviewSpecialisation().ForbiddenCapabilities
                    .Where(item => item != capability)
                    .ToArray()
            };

            var issues = Validator.Validate(definition);

            AssertHasIssue(issues, AgentSpecialisationValidator.DangerousCapabilityMustBeForbidden);
        }
    }

    [TestMethod]
    public void AgentSpecialisation_RequiredCapabilityCannotAlsoBeForbidden()
    {
        var definition = BuildReviewSpecialisation() with
        {
            RequiredCapabilities = new[] { AgentCapability.CreateCriticFinding },
            ForbiddenCapabilities = BuildRequiredForbiddenCapabilities()
                .Concat(new[] { AgentCapability.CreateCriticFinding })
                .ToArray()
        };

        var issues = Validator.Validate(definition);

        AssertHasIssue(issues, AgentSpecialisationValidator.ForbiddenCapabilityOverride);
    }

    [TestMethod]
    public void AgentSpecialisation_RequiresAtLeastOneTypedOutput()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation() with
        {
            OutputRequirements = Array.Empty<AgentSpecialisationOutputRequirement>()
        });

        AssertHasIssue(issues, AgentSpecialisationValidator.OutputRequirementRequired);
    }

    [TestMethod]
    public void AgentSpecialisation_OutputCannotCreateAuthorityRuntimeActionOrMemoryPromotion()
    {
        var invalidOutputs = new[]
        {
            new AgentSpecialisationOutputRequirement
            {
                OutputType = "CriticReviewResult",
                Description = "Review-only finding output.",
                RequiresHumanReview = true,
                MustBeReviewOnly = true,
                MayCreateAuthority = true
            },
            new AgentSpecialisationOutputRequirement
            {
                OutputType = "CriticReviewResult",
                Description = "Review-only finding output.",
                RequiresHumanReview = true,
                MustBeReviewOnly = true,
                MayCreateRuntimeAction = true
            },
            new AgentSpecialisationOutputRequirement
            {
                OutputType = "MemoryImprovementDetectionResult",
                Description = "Proposal-only memory-improvement output.",
                RequiresHumanReview = true,
                MustBeProposalOnly = true,
                MayPromoteMemory = true
            }
        };

        foreach (var output in invalidOutputs)
        {
            var issues = Validator.Validate(BuildReviewSpecialisation() with
            {
                OutputRequirements = new[] { output }
            });

            Assert.IsTrue(
                issues.Any(issue =>
                    issue.Code == AgentSpecialisationValidator.OutputAuthorityBlocked ||
                    issue.Code == AgentSpecialisationValidator.OutputRuntimeActionBlocked ||
                    issue.Code == AgentSpecialisationValidator.OutputPromotionBlocked),
                FormatIssues(issues));
        }
    }

    [TestMethod]
    public void AgentSpecialisation_OutputMustRequireHumanReview()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation() with
        {
            OutputRequirements = new[]
            {
                new AgentSpecialisationOutputRequirement
                {
                    OutputType = "CriticReviewResult",
                    Description = "Review-only finding output.",
                    RequiresHumanReview = false,
                    MustBeReviewOnly = true
                }
            }
        });

        AssertHasIssue(issues, AgentSpecialisationValidator.OutputHumanReviewRequired);
    }

    [TestMethod]
    public void AgentSpecialisation_CriticOutputMustBeReviewOnly()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation() with
        {
            OutputRequirements = new[]
            {
                new AgentSpecialisationOutputRequirement
                {
                    OutputType = "CriticReviewResult",
                    Description = "Review output.",
                    RequiresHumanReview = true,
                    MustBeReviewOnly = false
                }
            }
        });

        AssertHasIssue(issues, AgentSpecialisationValidator.OutputRequirementInvalid);
    }

    [TestMethod]
    public void AgentSpecialisation_MemoryImprovementOutputMustBeProposalOnly()
    {
        var issues = Validator.Validate(BuildMemoryImprovementSpecialisation() with
        {
            OutputRequirements = new[]
            {
                new AgentSpecialisationOutputRequirement
                {
                    OutputType = "MemoryImprovementDetectionResult",
                    Description = "Memory-improvement detection output.",
                    RequiresHumanReview = true,
                    MustBeProposalOnly = false
                }
            }
        });

        AssertHasIssue(issues, AgentSpecialisationValidator.OutputRequirementInvalid);
    }

    [TestMethod]
    public void AgentSpecialisation_AuthorityInputsAreWarningsNotPower()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation() with
        {
            InputRequirements = new[]
            {
                new AgentSpecialisationInputRequirement
                {
                    InputType = "GovernanceBundle",
                    Description = "Evidence bundle for review.",
                    AllowedAuthorityReferenceTypes = new[] { "HumanApprovalEvidence", "GovernanceDecision", "PolicyDecision" }
                }
            }
        });

        AssertNoErrors(issues);
        AssertHasIssue(issues, AgentSpecialisationValidator.InputAuthorityConsumptionDeclared, AgentDefinitionValidator.SeverityWarning);
    }

    [TestMethod]
    public void AgentSpecialisation_AuthorityEvidenceIsWarningNotPower()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation() with
        {
            EvidenceRequirements = new[]
            {
                new AgentSpecialisationEvidenceRequirement
                {
                    EvidenceType = "ApprovalTrace",
                    Description = "Evidence bundle for review.",
                    AllowedAuthorityEvidenceTypes = new[] { "HumanApprovalEvidence", "GovernanceDecision", "PolicyDecision" }
                }
            }
        });

        AssertNoErrors(issues);
        AssertHasIssue(issues, AgentSpecialisationValidator.EvidenceAuthorityConsumptionDeclared, AgentDefinitionValidator.SeverityWarning);
    }

    [TestMethod]
    public void AgentSpecialisation_RequiresCommonValidationRequirements()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation() with
        {
            ValidationRequirements = new[]
            {
                new AgentSpecialisationValidationRequirement
                {
                    ValidatorName = "CriticReviewResultValidator",
                    Description = "Validates typed critic output."
                }
            }
        });

        AssertHasIssue(issues, AgentSpecialisationValidator.ValidationRequirementRequired);
    }

    [TestMethod]
    public void AgentSpecialisation_ValidationRequirementCannotImplyExecutionOrAuthority()
    {
        var unsafeValidators = new[]
        {
            "ToolRouterValidator",
            "PromptRunnerValidator",
            "AuthorityGrantValidator",
            "ApprovalGrantValidator",
            "PromotionValidator"
        };

        foreach (var validatorName in unsafeValidators)
        {
            var issues = Validator.Validate(BuildReviewSpecialisation() with
            {
                ValidationRequirements = BuildValidationRequirements("CriticReviewResultValidator")
                    .Concat(new[]
                    {
                        new AgentSpecialisationValidationRequirement
                        {
                            ValidatorName = validatorName,
                            Description = "Unsafe validation boundary."
                        }
                    })
                    .ToArray()
            });

            AssertHasIssue(issues, AgentSpecialisationValidator.ValidationRequirementUnsafe);
        }
    }

    [TestMethod]
    public void AgentSpecialisation_RequiresMandatoryForbiddenBehaviours()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation() with
        {
            ForbiddenBehaviours = BuildForbiddenBehaviours()
                .Where(behaviour => behaviour.Behaviour != "StorePrivateReasoning")
                .ToArray()
        });

        AssertHasIssue(issues, AgentSpecialisationValidator.ForbiddenBehaviourRequired);
    }

    [TestMethod]
    public void AgentSpecialisation_ForbiddenBehaviourCannotBeOptional()
    {
        var issues = Validator.Validate(BuildReviewSpecialisation() with
        {
            ForbiddenBehaviours = BuildForbiddenBehaviours()
                .Select(behaviour => behaviour.Behaviour == "RunTool"
                    ? behaviour with { Required = false }
                    : behaviour)
                .ToArray()
        });

        AssertHasIssue(issues, AgentSpecialisationValidator.ForbiddenBehaviourOptional);
    }

    [TestMethod]
    public void AgentSpecialisation_RejectsUnsafeIdentifiers()
    {
        var unsafeIds = new[]
        {
            "critic.approval",
            "critic.promote",
            "critic.execute",
            "critic.runtime",
            "critic.mutate",
            "critic.admin",
            "critic.authority",
            "critic.god",
            "critic.root",
            "critic.override",
            "critic.bypass"
        };

        foreach (var id in unsafeIds)
        {
            var issues = Validator.Validate(BuildReviewSpecialisation() with
            {
                SpecialisationId = id
            });

            AssertHasIssue(issues, AgentSpecialisationValidator.SpecialisationIdUnsafe);
        }
    }

    [TestMethod]
    public void AgentSpecialisation_RejectsRawPrivateReasoningText()
    {
        var markers = new[]
        {
            "raw prompt",
            "raw completion",
            "chain-of-thought",
            "scratchpad",
            "private reasoning",
            "hidden reasoning",
            "hidden deliberation",
            "system prompt",
            "developer prompt"
        };

        foreach (var marker in markers)
        {
            var issues = Validator.Validate(BuildReviewSpecialisation() with
            {
                Description = $"This specialisation asks for {marker}."
            });

            AssertHasIssue(issues, AgentSpecialisationValidator.RawPrivateReasoningBlocked);
        }
    }

    [TestMethod]
    public void AgentSpecialisation_RejectsAuthorityClaimText()
    {
        var markers = new[]
        {
            "approval granted",
            "human approved",
            "approved for execution",
            "policy cleared",
            "authoritative for action",
            "grant authority",
            "override policy",
            "bypass governance",
            "promote memory",
            "accepted memory",
            "system rule"
        };

        foreach (var marker in markers)
        {
            var issues = Validator.Validate(BuildReviewSpecialisation() with
            {
                Description = $"This specialisation says {marker}."
            });

            AssertHasIssue(issues, AgentSpecialisationValidator.AuthorityClaimBlocked);
        }
    }

    [TestMethod]
    public void AgentSpecialisation_CompatibilityRejectsWrongAgentId()
    {
        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            BuildReviewSpecialisation() with
            {
                AppliesToAgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId
            });

        Assert.IsFalse(result.IsCompatible);
        AssertHasIssue(result.Issues, AgentSpecialisationValidator.CompatibilityAgentIdMismatch);
    }

    [TestMethod]
    public void AgentSpecialisation_CompatibilityRejectsWrongAgentKind()
    {
        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            BuildReviewSpecialisation() with
            {
                RequiredAgentKind = AgentKind.ProposalAgent
            });

        Assert.IsFalse(result.IsCompatible);
        AssertHasIssue(result.Issues, AgentSpecialisationValidator.CompatibilityKindMismatch);
    }

    [TestMethod]
    public void AgentSpecialisation_CompatibilityRejectsWrongExecutionMode()
    {
        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            BuildReviewSpecialisation() with
            {
                RequiredExecutionMode = AgentExecutionMode.ProposalOnly
            });

        Assert.IsFalse(result.IsCompatible);
        AssertHasIssue(result.Issues, AgentSpecialisationValidator.CompatibilityExecutionModeMismatch);
    }

    [TestMethod]
    public void AgentSpecialisation_CompatibilityRejectsMissingRequiredCapability()
    {
        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            BuildReviewSpecialisation() with
            {
                RequiredCapabilities = new[] { AgentCapability.CreateMemoryProposal }
            });

        Assert.IsFalse(result.IsCompatible);
        AssertHasIssue(result.Issues, AgentSpecialisationValidator.CompatibilityRequiredCapabilityMissing);
    }

    [TestMethod]
    public void AgentSpecialisation_CompatibilityRejectsRequiredCapabilityForbiddenByAgent()
    {
        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            BuildReviewSpecialisation() with
            {
                RequiredCapabilities = new[] { AgentCapability.RunTool }
            });

        Assert.IsFalse(result.IsCompatible);
        AssertHasIssue(result.Issues, AgentSpecialisationValidator.CompatibilityRequiredCapabilityForbidden);
    }

    [TestMethod]
    public void AgentSpecialisation_CompatibilityRejectsForbiddenCapabilityPresentOnAgent()
    {
        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            BuildReviewSpecialisation() with
            {
                ForbiddenCapabilities = BuildRequiredForbiddenCapabilities()
                    .Concat(new[] { AgentCapability.CreateCriticFinding })
                    .ToArray()
            });

        Assert.IsFalse(result.IsCompatible);
        AssertHasIssue(result.Issues, AgentSpecialisationValidator.CompatibilityForbiddenCapabilityConflict);
    }

    [TestMethod]
    public void AgentSpecialisation_CompatibilityAllowsForbiddenCapabilityAbsentFromAgent()
    {
        var agentCapabilities = AgentDefinitionCatalog.IndependentCriticAgent.Capabilities ?? new HashSet<AgentCapability>();
        var agentForbiddenCapabilities = AgentDefinitionCatalog.IndependentCriticAgent.ForbiddenCapabilities ?? new HashSet<AgentCapability>();
        var absentCapability = Enum.GetValues<AgentCapability>()
            .First(capability =>
                !agentCapabilities.Contains(capability) &&
                !agentForbiddenCapabilities.Contains(capability));

        var result = Validator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            BuildReviewSpecialisation() with
            {
                ForbiddenCapabilities = BuildRequiredForbiddenCapabilities()
                    .Concat(new[] { absentCapability })
                    .ToArray()
            });

        Assert.IsTrue(result.IsCompatible, FormatIssues(result.Issues));
        AssertNoErrors(result.Issues);
    }

    [TestMethod]
    public void AgentSpecialisation_DoesNotRegisterConcreteProfilesInAgentDefinitionCatalog()
    {
        var catalogPath = Path.Combine(FindRepositoryRoot(), "IronDev.Core", "Agents", "AgentDefinitionCatalog.cs");
        var catalogText = File.ReadAllText(catalogPath);

        AssertDoesNotContain(catalogText, "AgentSpecialisationDefinition");
        AssertDoesNotContain(catalogText, "AgentSpecialisationValidator");
    }

    [TestMethod]
    public void AgentSpecialisation_ProductionContractsDoNotIntroduceRuntimeOrStorageBoundaries()
    {
        var productionPath = Path.Combine(FindRepositoryRoot(), "IronDev.Core", "Agents", "AgentSpecialisationModels.cs");
        var text = File.ReadAllText(productionPath);
        var forbiddenTokens = new[]
        {
            "IAgentRuntime",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentPromptRunner",
            "AgentToolRouter",
            "ExecuteAgentAsync",
            "RunAgentAsync",
            "IChatCompletion",
            "OpenAI",
            "Anthropic",
            "Gemini",
            "SqlConnection",
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "Weaviate",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "ManualIndependentCriticAgentService",
            "ManualMemoryImprovementAgentService",
            "SqlAgentRunAuditEnvelopeStore",
            "SqlAgentRunAuditEnvelopeReadRepository",
            "ICollectiveMemoryPromotionService",
            "SqlCollectiveMemoryPromotionService",
            "IMemoryImprovementProposalStore",
            "SqlMemoryImprovementProposalStore"
        };

        foreach (var token in forbiddenTokens)
        {
            AssertDoesNotContain(text, token);
        }
    }

    private static AgentSpecialisationDefinition BuildReviewSpecialisation() => new()
    {
        SpecialisationId = "critic.review.findings",
        Name = "Critical review findings",
        Description = "Produces typed review findings from existing evidence only.",
        Kind = AgentSpecialisationKind.CriticalReview,
        AppliesToAgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
        RequiredAgentKind = AgentKind.ReviewAgent,
        RequiredExecutionMode = AgentExecutionMode.OutOfBandReviewOnly,
        RequiredCapabilities = new[]
        {
            AgentCapability.CreateCriticFinding,
            AgentCapability.WarnExecution
        },
        ForbiddenCapabilities = BuildRequiredForbiddenCapabilities(),
        Purposes = new[]
        {
            "Review evidence and produce non-authoritative findings.",
            "Surface blockers for human review without blocking execution directly."
        },
        InputRequirements = new[]
        {
            new AgentSpecialisationInputRequirement
            {
                InputType = "AgentRunAuditEnvelope",
                Description = "Durable audit envelope to review."
            }
        },
        EvidenceRequirements = new[]
        {
            new AgentSpecialisationEvidenceRequirement
            {
                EvidenceType = "AgentRunAuditEnvelope",
                Description = "Audit evidence for critic review."
            }
        },
        OutputRequirements = new[]
        {
            new AgentSpecialisationOutputRequirement
            {
                OutputType = "CriticReviewResult",
                Description = "Review-only critic result.",
                RequiresHumanReview = true,
                MustBeReviewOnly = true
            }
        },
        ValidationRequirements = BuildValidationRequirements("CriticReviewResultValidator"),
        ForbiddenBehaviours = BuildForbiddenBehaviours(),
        AuthorityBoundary = AgentSpecialisationAuthorityBoundary.None
    };

    private static AgentSpecialisationDefinition BuildMemoryImprovementSpecialisation() => new()
    {
        SpecialisationId = "memory.improvement.detection",
        Name = "Memory improvement detection",
        Description = "Finds candidate memory improvements and emits proposal-only outputs.",
        Kind = AgentSpecialisationKind.MemoryImprovementDetection,
        AppliesToAgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
        RequiredAgentKind = AgentKind.ProposalAgent,
        RequiredExecutionMode = AgentExecutionMode.ProposalOnly,
        RequiredCapabilities = new[]
        {
            AgentCapability.CreateMemoryProposal
        },
        ForbiddenCapabilities = BuildRequiredForbiddenCapabilities(),
        Purposes = new[]
        {
            "Detect memory improvement candidates without changing memory.",
            "Create proposal-only outputs for governed review."
        },
        InputRequirements = new[]
        {
            new AgentSpecialisationInputRequirement
            {
                InputType = "AgentMemoryRunReport",
                Description = "Scoped memory report projection."
            }
        },
        EvidenceRequirements = new[]
        {
            new AgentSpecialisationEvidenceRequirement
            {
                EvidenceType = "MemoryInfluenceRecord",
                Description = "Scoped evidence for memory improvement proposals."
            }
        },
        OutputRequirements = new[]
        {
            new AgentSpecialisationOutputRequirement
            {
                OutputType = "MemoryImprovementDetectionResult",
                Description = "Proposal-only detection result.",
                RequiresHumanReview = true,
                MustBeProposalOnly = true
            },
            new AgentSpecialisationOutputRequirement
            {
                OutputType = "MemoryImprovementProposalDraft",
                Description = "Proposal-only draft.",
                RequiresHumanReview = true,
                MustBeProposalOnly = true
            }
        },
        ValidationRequirements = BuildValidationRequirements("MemoryImprovementDetectionResultValidator"),
        ForbiddenBehaviours = BuildForbiddenBehaviours(),
        AuthorityBoundary = AgentSpecialisationAuthorityBoundary.None
    };

    private static AgentCapability[] BuildRequiredForbiddenCapabilities() =>
        new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.CallExternalSystem,
            AgentCapability.PromoteCollectiveMemory,
            AgentCapability.RepresentHumanApproval,
            AgentCapability.RepresentHumanPromotionDecision,
            AgentCapability.BlockExecution
        };

    private static AgentSpecialisationValidationRequirement[] BuildValidationRequirements(string outputValidator) =>
        new[]
        {
            new AgentSpecialisationValidationRequirement
            {
                ValidatorName = "AgentDefinitionValidator",
                Description = "Validates base agent identity and authority boundary."
            },
            new AgentSpecialisationValidationRequirement
            {
                ValidatorName = "AgentRunAuditEnvelopeValidator",
                Description = "Validates durable audit envelope safety."
            },
            new AgentSpecialisationValidationRequirement
            {
                ValidatorName = "ThoughtLedgerSafetyValidator",
                Description = "Validates thought-ledger evidence safety."
            },
            new AgentSpecialisationValidationRequirement
            {
                ValidatorName = outputValidator,
                Description = "Validates typed specialisation output."
            }
        };

    private static AgentSpecialisationForbiddenBehaviour[] BuildForbiddenBehaviours() =>
        new[]
        {
            BuildForbiddenBehaviour("RunTool"),
            BuildForbiddenBehaviour("MutateSource"),
            BuildForbiddenBehaviour("CallExternalSystem"),
            BuildForbiddenBehaviour("PromoteCollectiveMemory"),
            BuildForbiddenBehaviour("RepresentHumanApproval"),
            BuildForbiddenBehaviour("RepresentHumanPromotionDecision"),
            BuildForbiddenBehaviour("OverridePolicy"),
            BuildForbiddenBehaviour("BypassGovernance"),
            BuildForbiddenBehaviour("CreateAuthority"),
            BuildForbiddenBehaviour("CreateRuntimeAction"),
            BuildForbiddenBehaviour("StoreRawPrompt"),
            BuildForbiddenBehaviour("StoreRawCompletion"),
            BuildForbiddenBehaviour("StoreChainOfThought"),
            BuildForbiddenBehaviour("StoreScratchpad"),
            BuildForbiddenBehaviour("StorePrivateReasoning")
        };

    private static AgentSpecialisationForbiddenBehaviour BuildForbiddenBehaviour(string behaviour) =>
        new()
        {
            Behaviour = behaviour,
            Reason = $"{behaviour} is outside this specialisation boundary.",
            Required = true
        };

    private static void AssertNoErrors(IReadOnlyCollection<AgentDefinitionValidationIssue> issues)
    {
        Assert.IsFalse(
            issues.Any(issue => issue.Severity == AgentDefinitionValidator.SeverityError),
            FormatIssues(issues));
    }

    private static void AssertHasIssue(
        IReadOnlyCollection<AgentDefinitionValidationIssue> issues,
        string code,
        string? severity = null)
    {
        Assert.IsTrue(
            issues.Any(issue =>
                issue.Code == code &&
                (severity is null || issue.Severity == severity)),
            $"Expected issue {code}. Actual: {FormatIssues(issues)}");
    }

    private static string FormatIssues(IEnumerable<AgentDefinitionValidationIssue> issues) =>
        string.Join("; ", issues.Select(issue => $"{issue.Severity}:{issue.Code}:{issue.Message}"));

    private static void AssertDoesNotContain(string text, string token)
    {
        Assert.IsFalse(
            text.Contains(token, StringComparison.Ordinal),
            $"Did not expect text to contain '{token}'.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
