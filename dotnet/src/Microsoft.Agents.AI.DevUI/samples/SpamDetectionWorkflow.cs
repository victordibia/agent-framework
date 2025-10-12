using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace Microsoft.Agents.AI.DevUI.Samples;

/// <summary>
/// Spam Detection Workflow Sample for DevUI
///
/// A comprehensive 5-step workflow with multiple executors that process, analyze,
/// detect spam, and handle email messages. Illustrates complex branching logic
/// and realistic processing delays.
///
/// Workflow Steps:
/// 1. Email Preprocessor - Cleans and prepares the email
/// 2. Content Analyzer - Analyzes email content and structure
/// 3. Spam Detector - Determines if the message is spam
/// 4a. Spam Handler - Processes spam messages (quarantine, log, remove)
/// 4b. Message Responder - Handles legitimate messages (validate, respond)
/// 5. Final Processor - Completes the workflow with logging and cleanup
/// </summary>
public static class SpamDetectionWorkflow
{
    public static async Task<Workflow<string>> CreateAsync()
    {
        // Create spam keywords list
        var spamKeywords = new[]
        {
            "spam", "advertisement", "offer", "click here",
            "winner", "congratulations", "urgent"
        };

        // Create all executors for the 5-step workflow
        var emailPreprocessor = new EmailPreprocessor();
        var contentAnalyzer = new ContentAnalyzer();
        var spamDetector = new SpamDetector(spamKeywords);
        var spamHandler = new SpamHandler();
        var messageResponder = new MessageResponder();
        var finalProcessor = new FinalProcessor();

        // Build the comprehensive 5-step workflow with branching logic
        var builder = new WorkflowBuilder(emailPreprocessor)
            .WithName("Email Spam Detector")
            .WithDescription("5-step email classification workflow with spam/legitimate routing");

        // Sequential flow through preprocessing, analysis, and detection
        builder.AddEdge(emailPreprocessor, contentAnalyzer);
        builder.AddEdge(contentAnalyzer, spamDetector);

        // Conditional branching based on spam detection result
        builder.AddEdge<SpamDetectorResponse>(spamDetector, spamHandler, condition: result => result?.IsSpam ?? false);
        builder.AddEdge<SpamDetectorResponse>(spamDetector, messageResponder, condition: result => result?.IsSpam == false);

        // Both paths converge at final processor
        builder.AddEdge(spamHandler, finalProcessor);
        builder.AddEdge(messageResponder, finalProcessor);

        return await builder.BuildAsync<string>();
    }
}

// ============== Data Models ==============

/// <summary>
/// Holds processed email content
/// </summary>
public record EmailContent(
    string OriginalMessage,
    string CleanedMessage,
    int WordCount,
    bool HasSuspiciousPatterns);

/// <summary>
/// Holds content analysis results
/// </summary>
public record ContentAnalysis(
    EmailContent EmailContent,
    float SentimentScore,
    bool ContainsLinks,
    bool HasAttachments,
    List<string> RiskIndicators);

/// <summary>
/// Holds spam detection results
/// </summary>
public record SpamDetectorResponse(
    ContentAnalysis Analysis,
    bool IsSpam,
    float ConfidenceScore,
    List<string> SpamReasons);

/// <summary>
/// Holds final processing result
/// </summary>
public record ProcessingResult(
    string OriginalMessage,
    string ActionTaken,
    float ProcessingTime,
    string Status,
    bool IsSpam,
    float ConfidenceScore,
    List<string> SpamReasons);

// ============== Executors ==============

/// <summary>
/// Step 1: Preprocesses and cleans email content
/// </summary>
internal sealed class EmailPreprocessor()
    : ReflectingExecutor<EmailPreprocessor>("EmailPreprocessor"),
      IMessageHandler<string, EmailContent>
{
    public async ValueTask<EmailContent> HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1500, cancellationToken); // Simulate preprocessing time

        // Simulate email cleaning
        var cleaned = message.Trim().ToLowerInvariant();
        var wordCount = message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        // Check for suspicious patterns
        var suspiciousPatterns = new[] { "urgent", "limited time", "act now", "free money" };
        var hasSuspicious = suspiciousPatterns.Any(pattern => cleaned.Contains(pattern));

        var result = new EmailContent(
            OriginalMessage: message,
            CleanedMessage: cleaned,
            WordCount: wordCount,
            HasSuspiciousPatterns: hasSuspicious
        );

        await context.AddEventAsync(
            new ExecutorCompletedEvent("EmailPreprocessor", $"Preprocessed email: {wordCount} words, suspicious: {hasSuspicious}"),
            cancellationToken);

        return result;
    }
}

/// <summary>
/// Step 2: Analyzes email content and structure
/// </summary>
internal sealed class ContentAnalyzer()
    : ReflectingExecutor<ContentAnalyzer>("ContentAnalyzer"),
      IMessageHandler<EmailContent, ContentAnalysis>
{
    public async ValueTask<ContentAnalysis> HandleAsync(
        EmailContent message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(2000, cancellationToken); // Simulate analysis time

        // Simulate content analysis
        var sentimentScore = message.HasSuspiciousPatterns ? 0.5f : 0.8f;
        var containsLinks = message.CleanedMessage.Contains("http") || message.CleanedMessage.Contains("www");
        var hasAttachments = message.CleanedMessage.Contains("attachment");

        // Build risk indicators
        var riskIndicators = new List<string>();
        if (message.HasSuspiciousPatterns) riskIndicators.Add("suspicious_language");
        if (containsLinks) riskIndicators.Add("contains_links");
        if (hasAttachments) riskIndicators.Add("has_attachments");
        if (message.WordCount < 10) riskIndicators.Add("too_short");

        var analysis = new ContentAnalysis(
            EmailContent: message,
            SentimentScore: sentimentScore,
            ContainsLinks: containsLinks,
            HasAttachments: hasAttachments,
            RiskIndicators: riskIndicators
        );

        await context.AddEventAsync(
            new ExecutorCompletedEvent("ContentAnalyzer", $"Analysis complete: {riskIndicators.Count} risk indicators"),
            cancellationToken);

        return analysis;
    }
}

/// <summary>
/// Step 3: Determines if a message is spam based on analysis
/// </summary>
internal sealed class SpamDetector : ReflectingExecutor<SpamDetector>, IMessageHandler<ContentAnalysis, SpamDetectorResponse>
{
    private readonly string[] _spamKeywords;

    public SpamDetector(string[] spamKeywords) : base("SpamDetector")
    {
        _spamKeywords = spamKeywords;
    }

    public async ValueTask<SpamDetectorResponse> HandleAsync(
        ContentAnalysis message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1800, cancellationToken); // Simulate detection time

        // Check for spam keywords
        var emailText = message.EmailContent.CleanedMessage;
        var keywordMatches = _spamKeywords.Where(kw => emailText.Contains(kw)).ToList();

        // Calculate spam probability
        var spamScore = 0.0f;
        var spamReasons = new List<string>();

        if (keywordMatches.Any())
        {
            spamScore += 0.4f;
            spamReasons.Add($"spam_keywords: {string.Join(", ", keywordMatches)}");
        }

        if (message.EmailContent.HasSuspiciousPatterns)
        {
            spamScore += 0.3f;
            spamReasons.Add("suspicious_patterns");
        }

        if (message.RiskIndicators.Count >= 3)
        {
            spamScore += 0.2f;
            spamReasons.Add("high_risk_indicators");
        }

        if (message.SentimentScore < 0.4f)
        {
            spamScore += 0.1f;
            spamReasons.Add("negative_sentiment");
        }

        var isSpam = spamScore >= 0.5f;

        var result = new SpamDetectorResponse(
            Analysis: message,
            IsSpam: isSpam,
            ConfidenceScore: spamScore,
            SpamReasons: spamReasons
        );

        await context.AddEventAsync(
            new ExecutorCompletedEvent("SpamDetector", $"Detection: {(isSpam ? "SPAM" : "LEGITIMATE")} (confidence: {spamScore:F2})"),
            cancellationToken);

        return result;
    }
}

/// <summary>
/// Step 4a: Handles spam messages with quarantine and logging
/// </summary>
internal sealed class SpamHandler()
    : ReflectingExecutor<SpamHandler>("SpamHandler"),
      IMessageHandler<SpamDetectorResponse, ProcessingResult>
{
    public async ValueTask<ProcessingResult> HandleAsync(
        SpamDetectorResponse message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (!message.IsSpam)
        {
            throw new InvalidOperationException("Message is not spam, cannot process with spam handler.");
        }

        await Task.Delay(2200, cancellationToken); // Simulate spam handling time

        var result = new ProcessingResult(
            OriginalMessage: message.Analysis.EmailContent.OriginalMessage,
            ActionTaken: "quarantined_and_logged",
            ProcessingTime: 2.2f,
            Status: "spam_handled",
            IsSpam: message.IsSpam,
            ConfidenceScore: message.ConfidenceScore,
            SpamReasons: message.SpamReasons
        );

        await context.AddEventAsync(
            new ExecutorCompletedEvent("SpamHandler", "Spam message quarantined and logged"),
            cancellationToken);

        return result;
    }
}

/// <summary>
/// Step 4b: Responds to legitimate messages
/// </summary>
internal sealed class MessageResponder()
    : ReflectingExecutor<MessageResponder>("MessageResponder"),
      IMessageHandler<SpamDetectorResponse, ProcessingResult>
{
    public async ValueTask<ProcessingResult> HandleAsync(
        SpamDetectorResponse message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (message.IsSpam)
        {
            throw new InvalidOperationException("Message is spam, cannot respond with message responder.");
        }

        await Task.Delay(2500, cancellationToken); // Simulate response time

        var result = new ProcessingResult(
            OriginalMessage: message.Analysis.EmailContent.OriginalMessage,
            ActionTaken: "responded_and_filed",
            ProcessingTime: 2.5f,
            Status: "message_processed",
            IsSpam: message.IsSpam,
            ConfidenceScore: message.ConfidenceScore,
            SpamReasons: message.SpamReasons
        );

        await context.AddEventAsync(
            new ExecutorCompletedEvent("MessageResponder", "Legitimate message processed and response sent"),
            cancellationToken);

        return result;
    }
}

/// <summary>
/// Step 5: Completes the workflow with final logging and cleanup
/// </summary>
internal sealed class FinalProcessor()
    : ReflectingExecutor<FinalProcessor>("FinalProcessor"),
      IMessageHandler<ProcessingResult, string>
{
    public async ValueTask<string> HandleAsync(
        ProcessingResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(1500, cancellationToken); // Simulate final processing time

        var totalTime = message.ProcessingTime + 1.5f;

        // Include classification details in completion message
        var classification = message.IsSpam ? "SPAM" : "LEGITIMATE";
        var reasons = message.SpamReasons.Any() ? string.Join(", ", message.SpamReasons) : "none";

        var completionMessage = $"Email classified as {classification} (confidence: {message.ConfidenceScore:F2}). " +
                               $"Reasons: {reasons}. " +
                               $"Action: {message.ActionTaken}, " +
                               $"Status: {message.Status}, " +
                               $"Total time: {totalTime:F1}s";

        await context.AddEventAsync(
            new ExecutorCompletedEvent("FinalProcessor", completionMessage),
            cancellationToken);

        return completionMessage;
    }
}
