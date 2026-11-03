# Completed answer identity evidence

Connector 0.9.44 and Desktop 0.1.25 resolve a local Codex conversation when a long turn has scrolled every submitted question out of the native console buffer. Previously discovery and per-send validation required a visible user-question anchor, so a readable saved conversation could still reject every send.

The additional route requires an empty native Codex composer, a completion marker, and an explicitly labelled assistant block immediately before that marker. Ordered substantial text must match exactly one currently owned original record's latest final answer. The minimum is 120 non-whitespace characters across multiple distinct substantial lines, or 160 on one line. Markdown link labels are compared as displayed; the original records remain unchanged. Every qualifying visible line must match the same original answer in order.

No saved binding alone authorizes input. Before an anchor-free send the complete live owner catalog is refreshed, including competing records. Duplicate answers, wrong targets, later original messages, open requests, native drafts, tools, commentary, missing completion, unlabelled fragments and generic short replies are rejected. Normal question-anchor matching is unchanged. Screen evidence identifies the complete original; it never substitutes for conversation history.

`CompletedAnswerEvidence.cs`, `EvidenceInlineLinks.cs` and `CompletedAnswerChecks.cs` are identical in the connector host and isolated Desktop worktree. Desktop adapters remain inside `DESKTOP` blocks. Changes do not alter Android binaries, pairing, transport, native terminal lifetime or SSH handling.

Validation: isolated completed-answer fixtures, existing parser/send checks, Desktop UI checks, and one read-only live owner/matching probe. No test message or Enter is sent into a user terminal. Live success means the per-send identity guard passes; actual user delivery remains a separate observation.
