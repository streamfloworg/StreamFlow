import { action, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";
import { streamFlowClient, SceneState } from "../streamflow-client";

type SceneControlSettings = {
	actionType?: "switch" | "next" | "prev";
	scene?: string;
	durationMs?: string;
};

@action({ UUID: "com.streamfloworg.streamflow-sd.scenecontrol" })
export class SceneControl extends SingletonAction<SceneControlSettings> {
	private activeActions = new Map<string, { action: any; settings: SceneControlSettings }>();
	private lastState: SceneState | null = null;

	constructor() {
		super();

		// Subscribe to real-time scene broadcasts
		streamFlowClient.onSceneState((state) => {
			this.lastState = state;
			for (const { action, settings } of this.activeActions.values()) {
				this.updateActionUI(action, settings, state);
			}
		});

		// Connection status changes
		streamFlowClient.onConnectionState((connState) => {
			if (connState !== "connected") {
				for (const { action } of this.activeActions.values()) {
					action.setTitle(connState === "connecting" ? "Connecting" : "Offline");
				}
			} else {
				this.refreshState();
			}
		});
	}

	override async onWillAppear(ev: WillAppearEvent<SceneControlSettings>): Promise<void> {
		this.activeActions.set(ev.action.id, { action: ev.action, settings: ev.payload.settings });

		if (streamFlowClient.getConnectionState() !== "connected") {
			ev.action.setTitle(streamFlowClient.getConnectionState() === "connecting" ? "Connecting" : "Offline");
		} else {
			if (this.lastState) {
				this.updateActionUI(ev.action, ev.payload.settings, this.lastState);
			} else {
				const state = await streamFlowClient.getScenes();
				if (state) {
					this.lastState = state;
					this.updateActionUI(ev.action, ev.payload.settings, state);
				} else {
					ev.action.setTitle("Connected");
				}
			}
		}
	}

	override onWillDisappear(ev: WillDisappearEvent<SceneControlSettings>): void {
		this.activeActions.delete(ev.action.id);
	}

	override async onKeyDown(ev: KeyDownEvent<SceneControlSettings>): Promise<void> {
		if (streamFlowClient.getConnectionState() !== "connected") {
			ev.action.setTitle("Connecting...");
			return;
		}

		const settings = ev.payload.settings;
		const actionType = settings.actionType ?? "switch";
		const durationMs = settings.durationMs ? Number(settings.durationMs) : undefined;

		if (actionType === "next") {
			await streamFlowClient.nextScene(durationMs);
		} else if (actionType === "prev") {
			await streamFlowClient.prevScene(durationMs);
		} else {
			const scene = settings.scene ?? "";
			await streamFlowClient.switchScene(scene, durationMs);
		}
	}

	private async refreshState(): Promise<void> {
		const state = await streamFlowClient.getScenes();
		if (state) {
			this.lastState = state;
			for (const { action, settings } of this.activeActions.values()) {
				this.updateActionUI(action, settings, state);
			}
		}
	}

	private updateActionUI(action: any, settings: SceneControlSettings, state: SceneState | null): void {
		const actionType = settings.actionType ?? "switch";

		if (actionType === "next") {
			if (state) {
				const activeScene = state.scenes.find(s => s.id === state.activeSceneId);
				const activeName = activeScene ? activeScene.name : "";
				action.setTitle(`Next\nScene\n${this.truncate(activeName)}`);
			} else {
				action.setTitle("Next\nScene");
			}
		} else if (actionType === "prev") {
			if (state) {
				const activeScene = state.scenes.find(s => s.id === state.activeSceneId);
				const activeName = activeScene ? activeScene.name : "";
				action.setTitle(`Prev\nScene\n${this.truncate(activeName)}`);
			} else {
				action.setTitle("Prev\nScene");
			}
		} else {
			// Switch scene
			const target = settings.scene ?? "";
			if (state) {
				const targetScene = state.scenes.find(s => s.id === target || s.name.toLowerCase() === target.toLowerCase());
				const activeScene = state.scenes.find(s => s.id === state.activeSceneId);

				const targetName = targetScene ? targetScene.name : target;
				const isActive = targetScene ? (state.activeSceneId === targetScene.id) : (activeScene ? activeScene.name.toLowerCase() === target.toLowerCase() : false);

				if (isActive) {
					action.setTitle(`${this.truncate(targetName)}\n[ACTIVE]`);
				} else {
					action.setTitle(`Go To\n${this.truncate(targetName)}`);
				}
			} else {
				action.setTitle(`Go To\n${this.truncate(target)}`);
			}
		}
	}

	private truncate(text: string, maxLen: number = 8): string {
		if (text.length > maxLen) {
			return text.substring(0, maxLen - 1) + "..";
		}
		return text;
	}
}
