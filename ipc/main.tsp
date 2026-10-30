import "@adofai-ipc/typespec";

@ipc.mod("orbitrender")
namespace OrbitRenderIpc {
  model RenderOptions {
    levelPath: string;
    preset?: string;
    width?: int32;
    height?: int32;
    targetFps?: int32;
    videoFps?: int32;
    bitrateMbps?: int32;
    videoCodec?: string;
    audioCodec?: string;
    bitDepth?: int32;
    endDelaySeconds?: float64;
    captureAudio?: boolean;
    audioGainDb?: float64;
    bgaMode?: boolean;
    showPlanetRings?: boolean;
    showSongTitle?: boolean;
    showCountdown?: boolean;
    showResultText?: boolean;
    showHitJudgments?: boolean;
  }
  model Health { ok: boolean; renderer: string; }
  model RenderAccepted { id: string; state: string; statusUrl: string; downloadUrl: string; }
  model Cancelled { id: string; state: string; }
  model RenderSettings {
    preset: string | null;
    width: int32 | null;
    height: int32 | null;
    targetFps: int32 | null;
    videoFps: int32 | null;
    fps: int32 | null;
    bitrateMbps: int32 | null;
    endDelaySeconds: float64 | null;
    audioGainDb: float64 | null;
    audioCodec: string | null;
    bgaMode: boolean | null;
    showPlanetRings: boolean | null;
    showSongTitle: boolean | null;
    showCountdown: boolean | null;
    showResultText: boolean | null;
    showHitJudgments: boolean | null;
    videoCodec: string | null;
    bitDepth: int32 | null;
  }
  model Job {
    id: string;
    state: string;
    levelPath: string;
    settings: RenderSettings | null;
    outputPath: string | null;
    totalFrames: safeint;
    capturedFrames: safeint;
    progress: float64;
    error: string | null;
    updatedUtc: string;
  }
  op health(): Health;
  op listJobs(): Job[];
  op createRender(options: RenderOptions): RenderAccepted;
  op renderStatus(id: string): Job;
  op cancelRender(id: string): Cancelled;
  op downloadRender(id: string): ipc.File;
}
