using System.Runtime.CompilerServices;

// The diagnostic assembly exercises the real adapter without making the
// campaign-facing API public or including the harness in the normal package.
[assembly: InternalsVisibleTo("Lodeworks.Phase1Harness")]

