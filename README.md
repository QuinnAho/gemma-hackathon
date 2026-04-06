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

## High-Level Flow

1. Unity captures simulation state.
2. Unity captures trainee speech.
3. Cactus transcribes speech locally.
4. Gemma receives state, user input, and available tools.
5. Gemma responds and optionally calls simulation functions.
6. Unity executes those functions and updates the simulation.
7. The trainee sees the updated world and AI feedback.

## Build Order

1. Prove Quest 3 native inference works.
2. Build the reusable framework layer.
3. Connect the voice pipeline.
4. Implement one complete scenario on top.
5. Test, optimize, and package the demo.

## Scope Discipline

The focus is:
- on-device AI
- Unity + Quest 3
- reusable simulation framework
- one strong example use case

The focus is not:
- cloud-first inference
- multiple scenarios
- complex avatar/TTS systems
- unnecessary polish before the core loop works
