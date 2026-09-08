using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WealthLedger.Api.Tests")]

// The UI owns its own stable-code mapping so its assembly need not reference
// Infrastructure or API contracts. Its tests pin that mapping against these
// transport codes so the two cannot drift apart unnoticed.
[assembly: InternalsVisibleTo("WealthLedger.UI.Tests")]
