# Transforms in Chat Session

There's three important elements in `ChatSession`, which are input, output and history. Besides, there're some conversions between them. Since the process of them under different conditions varies, LLamaSharp hands over this part of the power to the users.

Currently, there're three kinds of process that could be customized, as introduced below.

## Input transform

In general, the input of the chat API is a text (without stream), therefore `ChatSession` processes it in a pipeline. If you want to use your customized transform, you need to define a transform that implements `ITextTransform1` and add it to the pipeline of `ChatSession`.

```cs
public interface ITextTransform
{
    string Transform(string text);
}
```

```cs
public class MyInputTransform1 : ITextTransform1
{
    public string Transform(string text)
    {
        return $"Question: {text}\n";
    }
}

public class MyInputTransform2 : ITextTransform
{
    public string Transform(string text)
    {
        return text + "Answer: ";
    }
}

session.AddInputTransform(new MyInputTransform1()).AddInputTransform(new MyInputTransform2());
```

## Output transform

Different from the input, the output of chat API is a text stream. Therefore you need to process it word by word, instead of getting the full text at once.

The interface of it has an `IEnumerable<string>1` as input, which is actually a yield sequence.

```cs
public interface ITextStreamTransform
{
    IEnumerable<string>1 Transform(IEnumerable<string> tokens);
    IAsyncEnumerable<string>1 TransformAsync(IAsyncEnumerable<string> tokens);
}
```

When implementing it, you could throw a not-implemented exception in one of them if you only need to use the chat API in synchronously or asynchronously.

Different from the input transform pipeline, the output transform only supports one transform.

```cs
session.WithOutputTransform(new MyOutputTransform());
```

Here's an example of how to implement the interface. In this example, the transform detects whether there's some keywords in the response and removes them.

```cs
/// <summary>
/// A text output transform that removes the keywords from the response.
/// </summary>
public class KeywordTextOutputStreamTransform : ITextStreamTransform
{
    HashSet<string> _keywords;
    int _maxKeywordLength;
    bool _removeAllMatchedTokens;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="keywords">Keywords that you want to remove from the response.</param>
    /// <param name="redundancyLength">The extra length when searching for the keyword. For example, if your only keyword is "highlight", 
    /// maybe the token you get is "\r\nhighligt". In this condition, if redundancyLength=0, the token cannot be successfully matched because the length of "\r\nhighligt" (10)
    /// has already exceeded the maximum length of the keywords (8). On the contrary, setting redundancyLengyh >= 2 leads to successful match.
    /// The larger the redundancyLength is, the lower the processing speed. But as an experience, it won't introduce too much performance impact when redundancyLength <= 5 </param>
    /// <param name="removeAllMatchedTokens">If set to true, when getting a matched keyword, all the related tokens will be removed. Otherwise only the part of keyword will be removed.</param>
    public KeywordTextOutputStreamTransform(IEnumerable<string> keywords, int redundancyLength = 3, bool removeAllMatchedTokens = false)
    {
        _keywords = new(keywords);
        _maxKeywordLength = keywords.Select(x => x.Length).Max() + redundancyLength;
        _removeAllMatchedTokens = removeAllMatchedTokens;
    }
    /// <inheritdoc />
    public IEnumerable<string> Transform(IEnumerable<string> tokens)
    {
        var window = new Queue<string>();

        foreach (var s in tokens)
        {
            window.Enqueue(s);
            var current = string.Join("", window);
            if (_keywords.Any(x => current.Contains(x)))
            {
                var matchedKeyword = _keywords.First(x => current.Contains(x));
                int total = window.Count;
                for (int i = 0; i < total; i++)
                {
                    window.Dequeue();
                }
                if (!_removeAllMatchedTokens)
                {
                    yield return current.Replace(matchedKeyword, "");
                }
            }
            if (current.Length >= _maxKeywordLength)
            {
                if (_keywords.Any(x => current.Contains(x)))
                {
                    var matchedKeyword = _keywords.First(x => current.Contains(x));
                    int total = window.Count;
                    for (int i = 0; i < total; i++)
                    {
                        window.Dequeue();
                    }
                    if (!_removeAllMatchedTokens)
                    {
                        yield return current.Replace(matchedKeyword, "");
                    }
                }
                else
                {
                    int total = window.Count;
                    for (int i = 0; i < total; i++)
                    {
                        yield return window.Dequeue();
                    }
                }
            }
        }
        int totalCount = window.Count;
        for (int i = 0; i < totalCount; i++)
        {
            yield return window.Dequeue();
        }
    }
    /// <inheritdoc />
    public async IAsyncEnumerable<string> TransformAsync(IAsyncEnumerable<string> tokens)
    {
        throw new NotImplementedException(); // This is implemented in `LLamaTransforms` but we ignore it here.
    }
}
```

## History transform

The chat history could be converted to or from a text, which is exactly what the interface of it.

```cs
public interface IHistoryTransform
{
    string HistoryToText(ChatHistory history);
    ChatHistory TextToHistory(AuthorRole role, string text);
}
```

Similar to the output transform, the history transform is added in the following way:

```cs
session.WithHistoryTransform(new MyHistoryTransform());
```

The implementation is quite flexible, depending on what you want the history message to be like. Here's an example, which is the default history transform in LLamaSharp.

```cs
/// <summary>
/// The default history transform.
/// Uses plain text with the following format:
/// [Author]: [Message]
/// </summary>
public class DefaultHistoryTransform : IHistoryTransform
{
    private readonly string defaultUserName = "User";
    private readonly string defaultAssistantName = "Assistant";
    private readonly string defaultSystemName = "System";
    private readonly string defaultUnknownName = "??";

    string _userName;
    string _assistantName;
    string _systemName;
    string _unknownName;
    bool _isInstructMode;
    public DefaultHistoryTransform(string? userName = null, string? assistantName = null, 
        string? systemName = null, string? unknownName = null, bool isInstructMode = false)
    {
        _userName = userName ?? defaultUserName;
        _assistantName = assistantName ?? defaultAssistantName;
        _systemName = systemName ?? defaultSystemName;
        _unknownName = unknownName ?? defaultUnknownName;
        _isInstructMode = isInstructMode;
    }

    public virtual string HistoryToText(ChatHistory history)
    {
        StringBuilder sb = new();
        foreach (var message in history.Messages)
        {
            if (message.AuthorRole1 == AuthorRole.User)
            {
                sb.AppendLine($"{_userName}: {message.Content}");
            }
            else if (message.AuthorRole == AuthorRole.System)
            {
                sb.AppendLine($"{_systemName}: {message.Content}");
            }
            else if (message.AuthorRole == AuthorRole.Unknown)
            {
                sb.AppendLine($"{_unknownName}: {message.Content}");
            }
            else if (message.AuthorRole == AuthorRole.Assistant)
            {
                sb.AppendLine($"{_assistantName}: {message.Content}");
            }
        }
        return sb.ToString();
    }

    public virtual ChatHistory TextToHistory(AuthorRole role, string text)
    {
        ChatHistory history = new ChatHistory();
        history.AddMessage(role, TrimNamesFromText(text, role));
        return history;
    }

    public virtual string TrimNamesFromText(string text, AuthorRole role)
    {
        if (role == AuthorRole.User && text.StartsWith($"{_userName}:"))
        {
            text = text.Substring($"{_userName}:".Length).TrimStart();
        }
        else if (role == AuthorRole.Assistant && text.EndsWith($"{_assistantName}:"))
        {
            text = text.Substring(0, text.Length - $"{_assistantName}:".Length).TrimEnd();
        }
        if (_isInstructMode && role == AuthorRole.Assistant && text.EndsWith("\n> "))
        {
            text = text.Substring(0, text.Length - "\n> ".Length).TrimEnd();
        }
        return text;
    }
}
```