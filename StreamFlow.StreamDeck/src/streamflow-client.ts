import streamDeck from "@elgato/streamdeck";

export interface GlobalSettings {
	hostUrl?: string;
	apiKey?: string;
}

export type ConnectionState = "connected" | "disconnected" | "connecting";

export interface StreamState {
	streaming: boolean;
	recording: boolean;
	viewerCount: number;
}

export interface AudioState {
	source: string;
	volume: number;
	muted: boolean;
}

export interface MediaState {
	playing: boolean;
	title?: string;
	artist?: string;
	source: "spotify" | "local";
}

export interface Scene {
	id: string;
	name: string;
}

export interface SceneState {
	activeSceneId: string;
	scenes: Scene[];
}

class StreamFlowClient {
	private hostUrl: string = "http://localhost:8080";
	private apiKey: string = "";
	private ws: WebSocket | null = null;
	private wsConnectionState: ConnectionState = "disconnected";
	private reconnectTimeout: NodeJS.Timeout | null = null;

	private streamListeners = new Set<(state: StreamState) => void>();
	private audioListeners = new Set<(state: AudioState) => void>();
	private mediaListeners = new Set<(state: MediaState) => void>();
	private sceneListeners = new Set<(state: SceneState) => void>();
	private connectionListeners = new Set<(state: ConnectionState) => void>();

	constructor() {
		// Listen for global settings changes from Stream Deck
		streamDeck.settings.onDidReceiveGlobalSettings((ev) => {
			const settings = ev.settings as GlobalSettings;
			this.updateSettings(settings);
		});

		// Fetch initial settings
		streamDeck.settings.getGlobalSettings()
			.then((settings) => {
				this.updateSettings(settings as GlobalSettings);
			})
			.catch((err) => {
				streamDeck.logger.error("Failed to get initial global settings:", err);
			});
	}

	/**
	 * Update settings and re-establish connection if the host changes.
	 */
	private updateSettings(settings: GlobalSettings): void {
		const newHost = settings.hostUrl || "http://localhost:8080";
		const newKey = settings.apiKey || "";

		if (newHost !== this.hostUrl || newKey !== this.apiKey || !this.ws) {
			this.hostUrl = newHost;
			this.apiKey = newKey;
			streamDeck.logger.info(`Settings updated. Host: ${this.hostUrl}`);
			this.connectWebSocket();
		}
	}

	/**
	 * Connect to StreamFlow.Maker WebSocket for real-time state broadcasts.
	 */
	private connectWebSocket(): void {
		this.cleanupWebSocket();
		this.setConnectionState("connecting");

		// Construct WebSocket URL from HTTP Host
		let wsUrl = this.hostUrl.replace(/^http/, "ws");
		if (!wsUrl.endsWith("/ws")) {
			wsUrl = wsUrl.replace(/\/?$/, "/ws");
		}

		if (this.apiKey) {
			wsUrl += `?token=${encodeURIComponent(this.apiKey)}`;
		}

		try {
			streamDeck.logger.info(`Connecting to WebSocket at ${wsUrl}`);
			this.ws = new globalThis.WebSocket(wsUrl);

			this.ws.onopen = () => {
				streamDeck.logger.info("WebSocket connected successfully");
				this.setConnectionState("connected");
			};

			this.ws.onmessage = (event) => {
				try {
					const message = JSON.parse(event.data.toString());
					this.handleWebSocketMessage(message);
				} catch (e) {
					streamDeck.logger.error("Error parsing WebSocket message:", e);
				}
			};

			this.ws.onerror = (error) => {
				streamDeck.logger.error("WebSocket connection error:", error);
			};

			this.ws.onclose = (event) => {
				streamDeck.logger.info(`WebSocket closed: ${event.reason} (code: ${event.code})`);
				this.setConnectionState("disconnected");
				this.scheduleReconnect();
			};
		} catch (error) {
			streamDeck.logger.error("Error creating WebSocket client:", error);
			this.setConnectionState("disconnected");
			this.scheduleReconnect();
		}
	}

	private cleanupWebSocket(): void {
		if (this.ws) {
			this.ws.onopen = null;
			this.ws.onmessage = null;
			this.ws.onerror = null;
			this.ws.onclose = null;
			try {
				this.ws.close();
			} catch (e) {}
			this.ws = null;
		}
		if (this.reconnectTimeout) {
			clearTimeout(this.reconnectTimeout);
			this.reconnectTimeout = null;
		}
	}

	private scheduleReconnect(): void {
		if (this.reconnectTimeout) return;
		this.reconnectTimeout = setTimeout(() => {
			this.reconnectTimeout = null;
			this.connectWebSocket();
		}, 5000); // Attempt reconnection every 5 seconds
	}

	private setConnectionState(state: ConnectionState): void {
		this.wsConnectionState = state;
		this.connectionListeners.forEach((listener) => listener(state));
	}

	private handleWebSocketMessage(msg: { event: string; data: any }): void {
		if (!msg || typeof msg.event !== "string") return;

		switch (msg.event) {
			case "streamState":
				this.streamListeners.forEach((l) => l(msg.data as StreamState));
				break;
			case "audioState":
				this.audioListeners.forEach((l) => l(msg.data as AudioState));
				break;
			case "mediaState":
				this.mediaListeners.forEach((l) => l(msg.data as MediaState));
				break;
			case "sceneState":
				this.sceneListeners.forEach((l) => l(msg.data as SceneState));
				break;
			default:
				streamDeck.logger.warn(`Unknown WebSocket event received: ${msg.event}`);
		}
	}

	/**
	 * Send HTTP commands to StreamFlow.Maker application.
	 */
	private async sendCommand<T>(path: string, payload?: object): Promise<T | null> {
		const url = `${this.hostUrl}${path.startsWith("/") ? "" : "/"}${path}`;
		const headers: Record<string, string> = {
			"Content-Type": "application/json",
		};
		if (this.apiKey) {
			headers["Authorization"] = `Bearer ${this.apiKey}`;
		}

		try {
			const response = await fetch(url, {
				method: payload ? "POST" : "GET",
				headers,
				body: payload ? JSON.stringify(payload) : undefined,
			});

			if (!response.ok) {
				streamDeck.logger.error(`Command failed: POST ${path} returned status ${response.status}`);
				return null;
			}

			if (response.status === 204) {
				return {} as T;
			}

			return (await response.json()) as T;
		} catch (error) {
			streamDeck.logger.error(`Error communicating with StreamFlow.Maker API at ${url}:`, error);
			return null;
		}
	}

	// API Command Wrappers

	public async toggleStreaming(platform: string = "all"): Promise<any> {
		return this.sendCommand("/api/streaming/toggle", { platform });
	}

	public async toggleRecording(type: string = "stream"): Promise<any> {
		return this.sendCommand("/api/recording/toggle", { type });
	}

	public async getStreamStatus(): Promise<StreamState | null> {
		return this.sendCommand<StreamState>("/api/streaming/status");
	}

	public async toggleMute(source: string): Promise<any> {
		return this.sendCommand("/api/audio/mute/toggle", { source });
	}

	public async setVolume(source: string, value: number, mode: "set" | "increment" | "decrement" = "set"): Promise<any> {
		return this.sendCommand("/api/audio/volume", { source, value, mode });
	}

	public async getAudioStatus(): Promise<any> {
		return this.sendCommand("/api/audio/status");
	}

	public async controlMedia(action: "play" | "pause" | "next" | "prev" | "toggle", player: string = "auto"): Promise<any> {
		return this.sendCommand("/api/media/control", { action, player });
	}

	public async playSpotifyUri(uri: string): Promise<any> {
		return this.sendCommand("/api/spotify/play-uri", { uri });
	}

	public async playLocalFile(filePath: string, volume: number): Promise<any> {
		return this.sendCommand("/api/audio/play-file", { filePath, volume });
	}

	public async getScenes(): Promise<SceneState | null> {
		return this.sendCommand<SceneState>("/api/scenes");
	}

	public async switchScene(scene: string, durationMs?: number): Promise<SceneState | null> {
		return this.sendCommand<SceneState>("/api/scenes/switch", { scene, durationMs });
	}

	public async nextScene(durationMs?: number): Promise<SceneState | null> {
		return this.sendCommand<SceneState>("/api/scenes/next", { durationMs });
	}

	public async prevScene(durationMs?: number): Promise<SceneState | null> {
		return this.sendCommand<SceneState>("/api/scenes/prev", { durationMs });
	}

	// Subscription Methods

	public onStreamState(listener: (state: StreamState) => void): () => void {
		this.streamListeners.add(listener);
		return () => this.streamListeners.delete(listener);
	}

	public onAudioState(listener: (state: AudioState) => void): () => void {
		this.audioListeners.add(listener);
		return () => this.audioListeners.delete(listener);
	}

	public onMediaState(listener: (state: MediaState) => void): () => void {
		this.mediaListeners.add(listener);
		return () => this.mediaListeners.delete(listener);
	}

	public onSceneState(listener: (state: SceneState) => void): () => void {
		this.sceneListeners.add(listener);
		return () => this.sceneListeners.delete(listener);
	}

	public onConnectionState(listener: (state: ConnectionState) => void): () => void {
		this.connectionListeners.add(listener);
		// Fire immediately with current state
		listener(this.wsConnectionState);
		return () => this.connectionListeners.delete(listener);
	}

	public getConnectionState(): ConnectionState {
		return this.wsConnectionState;
	}
}

export const streamFlowClient = new StreamFlowClient();
