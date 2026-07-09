import { action, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";
import { streamFlowClient, AudioState } from "../streamflow-client";

type AudioControlSettings = {
	actionType?: "mute" | "volume";
	source?: string;
	volumeAction?: "set" | "increment" | "decrement";
	volumeValue?: number;
};

@action({ UUID: "com.streamfloworg.streamflow-sd.audiocontrol" })
export class AudioControl extends SingletonAction<AudioControlSettings> {
	private activeActions = new Map<string, { action: any; settings: AudioControlSettings }>();

	constructor() {
		super();

		// Subscribe to real-time audio broadcasts
		streamFlowClient.onAudioState((state) => {
			for (const { action, settings } of this.activeActions.values()) {
				const targetSource = settings.source || "microphone";
				if (state.source.toLowerCase() === targetSource.toLowerCase()) {
					this.updateActionUI(action, settings, state);
				}
			}
		});

		// Connection status changes
		streamFlowClient.onConnectionState((connState) => {
			if (connState !== "connected") {
				for (const { action } of this.activeActions.values()) {
					action.setTitle(connState === "connecting" ? "Connecting" : "Offline");
				}
			} else {
				this.refreshStates();
			}
		});
	}

	override async onWillAppear(ev: WillAppearEvent<AudioControlSettings>): Promise<void> {
		this.activeActions.set(ev.action.id, { action: ev.action, settings: ev.payload.settings });

		if (streamFlowClient.getConnectionState() === "connected") {
			const targetSource = ev.payload.settings.source || "microphone";
			const status = await streamFlowClient.getAudioStatus();
			if (status && Array.isArray(status.sources)) {
				const match = status.sources.find((s: any) => s.name.toLowerCase() === targetSource.toLowerCase());
				if (match) {
					this.updateActionUI(ev.action, ev.payload.settings, match);
					return;
				}
			}
			ev.action.setTitle(targetSource);
		} else {
			ev.action.setTitle(streamFlowClient.getConnectionState() === "connecting" ? "Connecting" : "Offline");
		}
	}

	override onWillDisappear(ev: WillDisappearEvent<AudioControlSettings>): void {
		this.activeActions.delete(ev.action.id);
	}

	override async onKeyDown(ev: KeyDownEvent<AudioControlSettings>): Promise<void> {
		if (streamFlowClient.getConnectionState() !== "connected") {
			ev.action.setTitle("Connecting...");
			return;
		}

		const settings = ev.payload.settings;
		const actionType = settings.actionType ?? "mute";
		const source = settings.source || "microphone";

		if (actionType === "volume") {
			const mode = settings.volumeAction ?? "set";
			const value = settings.volumeValue ?? 50;
			await streamFlowClient.setVolume(source, value, mode);
		} else {
			await streamFlowClient.toggleMute(source);
		}
	}

	private async refreshStates(): Promise<void> {
		const status = await streamFlowClient.getAudioStatus();
		if (status && Array.isArray(status.sources)) {
			for (const { action, settings } of this.activeActions.values()) {
				const targetSource = settings.source || "microphone";
				const match = status.sources.find((s: any) => s.name.toLowerCase() === targetSource.toLowerCase());
				if (match) {
					this.updateActionUI(action, settings, match);
				}
			}
		}
	}

	private updateActionUI(action: any, settings: AudioControlSettings, state: AudioState): void {
		const actionType = settings.actionType ?? "mute";
		const displayName = settings.source || "Mic";

		if (actionType === "volume") {
			const mode = settings.volumeAction ?? "set";
			if (state.muted) {
				action.setTitle(`${displayName}\nMuted\n(${state.volume}%)`);
			} else {
				if (mode === "increment") {
					action.setTitle(`${displayName}\n+${settings.volumeValue ?? 5}%\n(${state.volume}%)`);
				} else if (mode === "decrement") {
					action.setTitle(`${displayName}\n-${settings.volumeValue ?? 5}%\n(${state.volume}%)`);
				} else {
					action.setTitle(`${displayName}\n${state.volume}%`);
				}
			}
		} else {
			if (state.muted) {
				action.setTitle(`${displayName}\n[MUTED]`);
			} else {
				action.setTitle(`${displayName}\nActive`);
			}
		}
	}
}
