using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Vortex.Shared;

public static class VortexRoles
{
    public const string User = "User";
    public const string Support = "Support";
    public const string Administrator = "Administrator";
    public const string Owner = "Owner";

    public static readonly string[] All = [User, Support, Administrator, Owner];
}

public static class VortexFeatures
{
    public const string Chat = "chat";
    public const string PremiumChat = "premium-chat";
    public const string FileContext = "file-context";
    public const string ProjectContext = "project-context";
    public const string VoiceInput = "voice-input";
    public const string TextToSpeech = "text-to-speech";
    public const string LocalTools = "local-tools";
}

public static class SupportedFileTypes
{
    public static readonly string[] Extensions =
    [
        ".cs", ".axaml", ".xaml", ".json", ".md", ".txt", ".xml", ".yaml", ".yml",
        ".js", ".ts", ".html", ".css", ".py", ".cpp", ".h", ".ps1", ".bat"
    ];
}

public enum ChatRole { System, User, Assistant, Tool }
public enum LocalToolRiskLevel { Low, Medium, High, Critical }

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserProfileDto User);
public sealed record UserProfileDto(Guid Id, string Email, string DisplayName, string Role, string PlanName, long StorageQuotaBytes, long StorageUsedBytes);

public sealed record ChatSessionDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreateChatRequest(string? Title = null);
public sealed record RenameChatRequest(string Title);

public sealed record ChatMessageDto(Guid Id, Guid ChatSessionId, string Role, string Content, DateTimeOffset CreatedAt, string? ModelName, bool IsStreaming, string? ErrorMessage);
public sealed record AiChatMessage(string Role, string Content);

public sealed record ChatCompletionRequest(Guid? ChatSessionId, string Message, string? RequestedModel, string? SystemPrompt, IReadOnlyList<AttachedFileDto>? Files, bool Stream = true);
public sealed record ChatCompletionChunk(string Delta, bool IsFinal, string? ModelName = null, string? ErrorMessage = null, string? CorrelationId = null);
public sealed record AttachedFileDto(Guid Id, string FileName, string ContentType, long SizeBytes, string? ExtractedText = null);

public sealed record AiProviderDto(Guid Id, string Name, string Type, string BaseUrl, bool IsActive, int Priority);
public sealed record AiModelDto(Guid Id, Guid ProviderId, string Name, string DisplayName, bool IsPremium, bool SupportsStreaming, bool SupportsTools, int ContextWindowTokens);
public sealed record SubscriptionPlanDto(Guid Id, string Name, string DisplayName, long StorageQuotaBytes, int DailyRequestLimit, int MonthlyRequestLimit, bool IsActive);
public sealed record PlanModelPolicyDto(Guid Id, Guid PlanId, Guid ProviderId, Guid ModelId, int Priority, int DailyUsageLimit, int MonthlyUsageLimit, string FeatureName, Guid? FallbackProviderId, Guid? FallbackModelId, bool IsActive);
public sealed record FeatureEntitlementDto(Guid Id, Guid PlanId, string FeatureName, bool IsEnabled, int? Limit, bool RequiresConfirmation);
public sealed record StorageQuotaDto(long QuotaBytes, long UsedBytes, long CommittedQuotaBytes, long AvailablePhysicalBytes);

public sealed record LocalAgentHello(string AgentName, string Version, string Platform, IReadOnlyList<LocalToolDescriptor> Tools);
public sealed record LocalToolDescriptor(string Name, string Description, bool IsEnabled, bool RequiresConfirmation, LocalToolRiskLevel RiskLevel);
public sealed record LocalToolRequest(string RequestId, string ToolName, Dictionary<string, string> Arguments, DateTimeOffset ExpiresAt, string Signature, bool UserConfirmed);
public sealed record LocalToolResponse(string RequestId, bool Succeeded, string Message, string? Output = null);

public sealed record AudioDeviceDto(string Id, string Name, bool IsDefaultInput, bool IsDefaultOutput);
public sealed record SpeechToTextRequest(string AudioBase64, string ContentType, string? Language);
public sealed record SpeechToTextResponse(string Text, decimal Confidence);
public sealed record TextToSpeechRequest(string Text, string? Voice, string? Language);
public sealed record TextToSpeechResponse(bool Succeeded, string Message);

public static class SecretMasker
{
    public static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length <= 8) return "••••";
        return trimmed[..Math.Min(3, trimmed.Length)] + "-••••••••••••••••" + trimmed[^4..];
    }
}

public static class PathSafety
{
    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path)) return false;
        var normalized = path.Replace('\\', '/');
        return !normalized.Split('/').Any(part => part is ".." or "" || part.Contains('\0'));
    }
}
