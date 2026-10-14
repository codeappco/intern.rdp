# Relay security regression tests

Run from the repository: `powershell -ExecutionPolicy Bypass -File tests/RelaySecurity.Tests.ps1`.
Requires the .NET 10 SDK. The runner builds and starts a local relay on a temporary
loopback port, runs the security and existing E2EE suites, then stops its server.

By default `/ws` rejects every request containing an `Origin` header with HTTP
403 before WebSocket upgrade. Optional web clients require an explicit
`WebSocket:AllowedOrigins` array, for example in appsettings.json:

```json
{ "WebSocket": { "AllowedOrigins": ["https://remote.example.com"] } }
```

Only configured HTTP(S) origins match; scheme, host and effective port must match.
Paths, credentials, wildcard entries, empty/`null`, malformed and multiple origins
are rejected. Invalid configuration prevents startup. No address is allowlisted
in the production defaults. The test runner enables `https://trusted.example`
only for its temporary relay and also tests the default desktop-only policy.
The Windows ClientWebSocket client sends no Origin and remains supported with
either policy. Proxies must preserve Origin;
stripping it would defeat the browser restriction. This policy is not client
authentication: native programs can omit or forge headers. Owner approval and
server-assigned socket identity/session membership still authorize relay traffic.
Use HTTPS/WSS for public deployment; the regression server uses loopback HTTP.

Connection codes expire after five minutes by default. Deployments can shorten
this with `ConnectionCodeLifetimeSeconds` (clamped to 1–300 seconds). The runner
uses five seconds to exercise expiry without a five-minute wait. Expired codes
are rejected both when requesting a connection and when approving a pending
request; stale entries are removed during message processing. Disconnect cleanup
removes every code belonging to the socket. Approval atomically consumes the
specific request's code before sending session notifications; failed/partial
notifications never restore the code. Rejections do not consume unexpired codes.
Issuing a new code invalidates all previous codes for that socket, cancels any
pending requests tied to those codes, and notifies the requester with the existing
`baglanti_sonucu`/`sure_doldu` response. Stale approvals cannot revive those requests.

Disconnect cleanup removes owned codes, pending requests and active sessions.
It runs before replying to a received Close frame (and before protocol-error
close output), then safely runs again from `finally`. A disconnected pending
requester or owner causes the other participant to receive the existing
`baglanti_sonucu`/`sure_doldu` notification. State removal and pending cancellation
are serialized together; a late approval cannot resurrect the request.
Session removal is idempotent. All outgoing messages, including `oturum_bitti`,
are serialized into per-socket FIFO outboxes, with at most 64 queued messages
and 8 MiB retained including the active write. State transitions never await
network I/O under the shared state lock. Each network write has a two-second
deadline; failed writes or full queues abort the affected socket and trigger
normal receive-loop cleanup. Messages are never silently dropped (which would
break E2EE sequence checking). WebSocket keepalive uses a one-second interval
and a two-second pong timeout to detect silent peers. This provides bounded attempts to
notify connected peers, not guaranteed delivery across network loss: the protocol
has no acknowledgement or durable notification queue.
The five-second target assumes a responsive server and a reachable, reading peer;
heavy scheduling delays or congestion are not a hard real-time guarantee. Short
timeouts may disconnect clients on severely delayed networks. Deploy the updated
Windows client with this server: `ResponsiveWebSocket` keeps the transport receive
running while UI dialogs wait for human approval, so Ping/Pong processing does
not depend on the UI. Its incoming queue is bounded at 512 x 8192-byte chunks
(4 MiB, plus one active chunk); overflow closes the connection. Application ping
for latency display now lives for the server connection, not just the session.

The inbound message cap is exactly 2 MiB (2,097,152 bytes), summed across all
fragments and checked before buffering each chunk. A full message is assembled
before strict UTF-8 decoding and JSON processing; partial payloads are never
relayed. Exactly the cap is accepted, exceeding it closes with 1009. Binary input
closes with 1003 and invalid UTF-8 with 1007. Incomplete fragmented messages have
a 30-second completion deadline from the first fragment. Close output has a
two-second deadline and does not wait for a peer acknowledgement before cleanup.

Every `baglan` message uses a rolling 60-second budget before code lookup:
10 attempts per server-assigned WebSocket connection and 30 per transport IP.
Valid, invalid and malformed codes all count. Exhaustion returns the existing
`bilgi` message type with `hata: "hiz_siniri"`. New connections do not reset the
IP budget. E2EE relay, owner approval, ping and session teardown do not spend
this budget. IP partitions are bounded at 10,000 and expired partitions are
reclaimed on subsequent attempts; capacity exhaustion fails closed.

`kod_iste` now has a separate rolling 60-second generation budget with the same
limits: 10 requests per connection and 30 per transport IP. Reconnecting or
spoofing forwarding headers does not reset the IP budget. Rejection happens
before rotating codes or canceling pending approvals, preserving the last valid
code and the request already awaiting consent. The response is `bilgi` with
`hata: "hiz_siniri"`, `islem: "kod_uretimi"`, and a one-minute retry message.
Generation and connection-attempt budgets are independent; exhausting one does
not disable the other or interfere with established session traffic.

Invalid code attempts emit `ConnectionCodeRejected`; the first denial in a
limited burst emits `ConnectionAttemptLimited`. Events contain only a server
client ID and a keyed, process-local IP pseudonym. Submitted codes, display
names, forwarding headers and message contents are never included. Pseudonyms
change on restart. Repeated limit responses do not flood the audit log.
Generation throttling emits `ConnectionCodeGenerationLimited` using the same
safe peer pseudonym and server-issued client ID, once per limited burst.

The IP key comes from the TCP peer; arbitrary `X-Forwarded-For` and `X-Real-IP`
headers are ignored. Behind a proxy, the proxy's address shares a budget unless
trusted proxy forwarding is separately configured. Users sharing NAT also share
the IP budget. Limits are local to this server process and reset on restart;
multi-instance deployments need a shared limiter or equivalent gateway policy.

The suites check Origin rejection, native access, forged and duplicate approval,
competing requests, code replay during/after a session, rejection followed by
retry, fresh codes, disconnect and expiry, plus encrypted key/screen/input relay,
outsider injection, ciphertext integrity/replay, and plaintext fallback rejection.
They also test client/IP budgets, concurrent acquisition, reconnect/header spoof
bypasses, deterministic expiry/recovery, partition cleanup, optional web origins,
relay continuity while throttled, and audit-log content. Run just deterministic
policy tests with `dotnet run --project tests/RelaySecurity.Tests`.
The V3 suite runs on a separate temporary server to keep its attempt budget
independent. It covers rotation during pending approval, abrupt disconnects in
both roles, session reuse after cleanup, exact-limit UTF-8 fragmentation, limit
overflow without close acknowledgement, binary/invalid UTF-8 input, and abrupt
disconnect mid-message. Live network tests assert healthy-peer notification
within five seconds. The V4 suite additionally keeps TCP sockets open while a
local byte-forwarding proxy drops both directions or one direction (no FIN/RST).
Both session roles are tested, as are continued service to an unrelated client,
exactly one termination notification, and released membership for new requests.
The actual desktop transport is also left unconsumed for six seconds to simulate
an approval dialog without blocking heartbeat processing. This is a transport
test, not automated interaction with the WPF dialog.

E2EE review: the relay does not decrypt. The desktop's actual `SecureSession.cs`
is linked into the tests: ephemeral P-256 ECDH, transcript-bound HKDF-SHA256,
separate AES-256-GCM keys per direction, 128-bit tags, sequence-derived nonces,
and session/direction/sequence authenticated data. Manual full verification-code
comparison authenticates the peer keys; encryption alone does not authenticate
an untrusted relay's key exchange. `MainWindow.Security.cs` waits for both local
and encrypted peer confirmation before screen/control activation, and control
input still needs the owner's per-session control permission. These UI checks
were reviewed in source; automated tests exercise the crypto and wire relay.
An oversized plaintext now fails without consuming a sequence number or retaining
the temporary plaintext buffer. The tests verify maximum supported plaintext
(1,400,000 bytes including JSON) encrypts into a relay-compatible message and
decrypts successfully after an oversized attempt is rejected.

V4 validation (2026-10-10): observed silent-loss notifications at 3497, 3498,
3507 and 3512 ms on loopback; all lifecycle, Origin, rate-limit, audit, E2EE and
message-boundary regressions passed. Server, test projects and Windows client
built with zero warnings/errors. These results cover local source builds, not
previously published executables or the deployed server.

V5 adds stock .NET `ClientWebSocket` tests, independently of the desktop wrapper:
three successive codes, renewal while approval is pending, stale approvals,
replay after explicit session end, normal `CloseAsync` in both roles, and pending
request cleanup on normal close in both roles. Code rejection assertions require
the invalid-code response, not merely a busy or rate-limit response. A new
approved session verifies that cleanup actually releases the participants.
Each receive has a four-second deadline. Local normal-close measurements were
3 ms for owner close and less than 1 ms for viewer close (integer millisecond
timer output: 0 ms); these are loopback observations, not network guarantees.

The runner preserves separate `v3-results.log`, `v4-results.log`,
`v5-results.log` and per-suite server stdout/stderr files in
`.test-build/security-server/`. It always rebuilds and launches the local DLL;
the user's external V5 endpoint/binary was not identified, so these results do
not establish which version the external test used. Old-code reuse was not
reproduced on the current source. The V5 code change moves disconnect cleanup
ahead of close I/O and reports cancellation of pending requests; it preserves
the existing one-code-per-owner, single-use, Origin and rate-limit rules.

MITM verification hardening:

- The actual WPF key-exchange handler now opens `PeerVerificationDialog` after
  deriving the full 128-bit verification code. A generic Yes/No acknowledgement
  is no longer sufficient: the user must enter the other endpoint's entire code,
  obtained through an independent trusted channel. Formatting whitespace/hyphens
  and letter case are ignored, but the complete 32 hexadecimal digits must match.
- The instructions explicitly say to use a known person's phone call or an
  in-person comparison, not to copy the local code, and to cancel when comparison
  is impossible. Cancel receives initial focus; no affirmative default exists.
- `PeerVerification` is shared by production and tests. Screen/control sends
  and receives require local comparison, successfully sent encrypted readiness,
  and received encrypted peer readiness. Only readiness may be sent before all
  three conditions. There is no saved trust or automatic approval path.
- Wrong/empty input, dismissal and session cleanup revoke verification. The
  existing two-minute verification deadline also closes the dialog. Late approval
  cannot reactivate a closed verification instance. Explicit owner control
  permission remains required after E2EE activation.
- `VerificationTests` uses the actual crypto with two attacker ECDH exchanges
  and checks code mismatch, pre-verification screen/input/permission rejection,
  readiness ordering, duplicate readiness, and revoked verification.
- `WpfSecurity.Tests` references the real desktop project and executes the actual
  dialog Click handler, `SecureMessage`, `SecureSend` and cleanup via reflection.
  It tests correct/wrong input, dismissal, timeout-equivalent cleanup while the
  modal dialog is open, instructions/default action, and absence of screen tasks
  or input permission. The timeout test invokes the real cleanup callback path;
  it does not wait two minutes. These tests now run in `RelaySecurity.Tests.ps1`.

This change cannot prove the user's comparison channel is trustworthy or prevent
a user from deliberately copying the local code into the peer-code field. Correct
independent comparison remains the peer-authentication requirement; a successful
ECDH exchange or encrypted ready message alone is insufficient. Existing E2EE
cryptography, sequence/replay checks and relay session authorization are unchanged.

The consolidated runner also executes `CodeGenerationTests` on a fresh relay
and saves `generation-results.log`. It verifies both generation limits, renewed
code invalidation, preservation of pending approval after rejected generation,
single-use behavior, reconnect/header-spoof resistance, independent budgets,
continued ping handling, and suppression of repeated generation-limit logs.
Limiter window expiry/recovery, concurrency and partition bounds are covered
by the shared limiter's deterministic policy tests.
