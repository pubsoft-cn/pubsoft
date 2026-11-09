// DocsTool entry — repo doc/API ledger checker & regenerator:
//   api-surface blocks (reflection) + named snippets (regions) + PublicAPI.* baselines (RS0016/17 codefix).
// --check runs inside scripts/verify-before-push.sh; --sync regenerates all three ledgers.
return PubSoft.DocsTool.DocsTool.Run(args);
