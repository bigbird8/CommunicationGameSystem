# Communication Game System

A rehabilitation pressure game system with microcontroller, server bridge, and game client.

## Architecture

```
Microcontroller (AVR) ─── UART/COBS/CRC-8 ──→ Server (WPF) ─── TCP/JSON ──→ Game Client (WPF)
                      ←── UART/COBS/CRC-8 ───                ←── TCP/JSON ───
```

## Projects

### Full System (CommunicationGameSystem.sln)
- **Server** — WPF server with UART↔TCP bridge, game authority, configuration UI
- **Client** — WPF game client with modern UI, pause/resume/restart
- **Shared** — Common DTOs, enums, protocol constants

### Client-Only Distribution (CommunicationGameSystem.Client-Only.sln)
- **Client** — Game client only
- **Shared** — Required shared library
- Use this solution when distributing to players (no server code included)

## Building

**Requirements:**
- .NET 8 SDK
- Visual Studio 2022 (or Rider/VS Code with C# DevKit)

**Steps:**
1. Open `CommunicationGameSystem.sln` (full) or `CommunicationGameSystem.Client-Only.sln` (client only)
2. Build → Build Solution
3. Run Server first, then Client

## Running the Server

1. Start `CommunicationGame.Server.exe`
2. Configure:
   - **COM Port:** Select your virtual COM port (from Proteus COMPIM)
   - **Baud Rate:** 9600 (default)
   - **TCP Port:** 5000 (default)
3. Click **Start Server**
4. Monitor UART/TCP/Game status in the UI

## Running the Client

1. Ensure the server is running **and the MCU/UART is connected** first
2. Start `CommunicationGame.Client.Wpf.exe`
3. Enter server **Host** (127.0.0.1 for local) and **Port** (5000)
4. Click **Connect**
5. The game starts **only when all links are ready** — the UART/MCU must be
   connected on the server side. If it isn't, the server replies with a
   `NOT_READY` error instead of starting; connect the MCU and reconnect.
6. Use **Pause**, **Resume**, **Restart** buttons as needed

> **Start ordering:** power up / connect the microcontroller, start the server
> and confirm UART shows **Connected**, then connect the client. The game will
> not begin until every connection in the chain is established.

## Game Rules

- **Green Zone:** Pressure between 40–70
- **Win:** Accumulate 30 seconds in green zone
- **Lose:** 3 consecutive seconds outside green zone
- Pause/Resume preserves timer state
- Restart creates a new game session

## Microcontroller

The microcontroller code is in `Microcontroller/AtmelStudioProject/main.c` (or `microContorller_side/`).

**Target:** ATmega32 @ 8 MHz  
**Protocol:** UART 9600 baud, COBS framing, CRC-8 validation  
**Simulation:** Proteus with COMPIM virtual COM port

## Protocol Documentation

See `docs/protocol-design.md` for full UART and TCP protocol specifications.

## Distribution

**For full system (development/testing):**
- Share entire `CommunicationGameSystem/` folder
- Open `CommunicationGameSystem.sln`

**For client-only (players/testers):**
- Share only: `Client/`, `Shared/`, `CommunicationGameSystem.Client-Only.sln`, `README.md`
- Players open `CommunicationGameSystem.Client-Only.sln` and build
- They only need the client executable and `Shared.dll`

## Troubleshooting

**"No connection could be made because the target machine actively refused it"**
- Ensure the server is running first
- Check that TCP port matches (default 5000)
- Verify firewall isn't blocking the connection

**UART stuck on "Handshaking" (MCU keeps sending HELLO):**
- This was caused by the firmware losing received bytes during `_delay_ms()`
  calls (the ATmega32 has only a 2-byte hardware UART buffer). The firmware now
  uses an **interrupt-driven receive ring buffer**, so the server's WELCOME is
  no longer dropped and the handshake completes. Re-flash `main.cpp` if you see
  this on an older build.
- Also check COM port selection, Proteus COMPIM wiring, and baud rate (9600).

**Game won't start / "NOT_READY" error:**
- By design, the game starts only when all connections are made. Ensure the
  server shows UART **Connected** before connecting the client.

**Logs not appearing:**
- Logs appear in both the mini log (main window) and the full log window.
- The client now logs live pressure activity during play (throttled to ~1/sec
  plus every green/red transition), so the log window stays active mid-game.
- Click "Open Full Log" for detailed history.

---

## Changelog

### Bug-fix pass (2026-06-27)
- **UART handshake fix (firmware):** RX is now interrupt-driven with a ring
  buffer, fixing the MCU getting stuck in Handshaking / repeatedly sending HELLO.
- **Connection gating (server):** the game starts/restarts only when the UART is
  connected *and* a TCP client is present; otherwise a `NOT_READY` error is sent.
- **Client logging fix:** TCP log events are marshalled to the UI thread (fixes a
  cross-thread bug), and live gameplay/pressure is logged so the log window
  reflects activity while the game runs.

---

**License:** Educational project  
**Author:** [Your Name]  
**Course:** Communication Systems
