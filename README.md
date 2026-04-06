# Gemma Hackathon

This project is building a reusable, on-device, simulation-aware AI framework for Quest 3.

## What We’re Building

The system is split into a few layers:

1. Cactus-Quest bridge
   Load `libcactus.so` into Unity on Quest 3 and prove local Gemma + STT work end to end.

2. Generic simulation-aware AI framework
   A reusable C# layer that lets any simulation expose state, define callable tools, and run an AI conversation loop.

3. Voice pipeline
   Capture mic audio, transcribe it locally with Cactus, and display AI responses in-VR as text.

4. Example scenario
   A concrete training use case built on top of the generic framework to prove the architecture works in practice.

5. Submission polish
   Demo, benchmarks, documentation, and packaging.

## Scope

The focus is:
- on-device AI
- Unity + Quest 3
- reusable simulation framework
- one strong example use case
