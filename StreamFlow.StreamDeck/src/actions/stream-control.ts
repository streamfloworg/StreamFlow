import { action, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";
import { streamFlowClient, StreamState, ConnectionState } from "../streamflow-client";

type StreamControlSettings = {
	actionType?: "stream" | "record";
	platform?: "all" | "twitch" | "youtube" | "tiktok";
	recordingType?: "stream" | "clip";
};

@action({ UUID: "com.streamfloworg.streamflow-sd.streamcontrol" })
export class StreamControl extends SingletonAction<StreamControlSettings> {
	private activeActions = new Map<string, { action: any; settings: StreamControlSettings }>();

	constructor() {
		super();

		// Subscribe to real-time stream status broadcasts from StreamFlow.Maker
		streamFlowClient.onStreamState((state) => {
			for (const { action, settings } of this.activeActions.values()) {
				this.updateActionUI(action, settings, state);
			}
		});

		// Subscribe to connection state changes
		streamFlowClient.onConnectionState((connState) => {
			if (connState !== "connected") {
				for (const { action, settings } of this.activeActions.values()) {
					action.setTitle(connState === "connecting" ? "Connecting" : "Offline");
				}
			} else {
				// Refresh state immediately upon connection
				this.refreshState();
			}
		});
	}

	override async onWillAppear(ev: WillAppearEvent<StreamControlSettings>): Promise<void> {
		this.activeActions.set(ev.action.id, { action: ev.action, settings: ev.payload.settings });

		// Fetch current state and update UI
		if (streamFlowClient.getConnectionState() === "connected") {
			const status = await streamFlowClient.getStreamStatus();
			if (status) {
				this.updateActionUI(ev.action, ev.payload.settings, status);
			} else {
				ev.action.setTitle("Connected");
			}
		} else {
			ev.action.setTitle(streamFlowClient.getConnectionState() === "connecting" ? "Connecting" : "Offline");
		}
	}

	override onWillDisappear(ev: WillDisappearEvent<StreamControlSettings>): void {
		this.activeActions.delete(ev.action.id);
	}

	override async onKeyDown(ev: KeyDownEvent<StreamControlSettings>): Promise<void> {
		if (streamFlowClient.getConnectionState() !== "connected") {
			// If not connected, key down triggers a reconnect attempt
			ev.action.setTitle("Connecting...");
			return;
		}

		const settings = ev.payload.settings;
		const actionType = settings.actionType ?? "stream";

		if (actionType === "record") {
			const recType = settings.recordingType ?? "stream";
			await streamFlowClient.toggleRecording(recType);
		} else {
			const platform = settings.platform ?? "all";
			await streamFlowClient.toggleStreaming(platform);
		}
	}

	private async refreshState(): Promise<void> {
		const status = await streamFlowClient.getStreamStatus();
		if (status) {
			for (const { action, settings } of this.activeActions.values()) {
				this.updateActionUI(action, settings, status);
			}
		}
	}

	private updateActionUI(action: any, settings: StreamControlSettings, state: StreamState): void {
		const actionType = settings.actionType ?? "stream";

		if (actionType === "record") {
			const recType = settings.recordingType ?? "stream";
			if (state.recording) {
				action.setTitle(recType === "clip" ? "Clipping!" : "REC\n●");
			} else {
				action.setTitle(recType === "clip" ? "Clip" : "Record");
			}
		} else {
			const platform = settings.platform ?? "all";
			if (state.streaming) {
				const platformLabel = platform === "all" ? "" : `\n(${platform})`;
				action.setTitle(`LIVE\n${state.viewerCount} v${platformLabel}`);
			} else {
				const platformLabel = platform === "all" ? "Stream" : `Stream\n${platform}`;
				action.setTitle(platformLabel);
			}
		}
	}
}
