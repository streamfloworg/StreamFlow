import { action, KeyDownEvent, SingletonAction, WillAppearEvent, WillDisappearEvent } from "@elgato/streamdeck";
import { streamFlowClient, MediaState } from "../streamflow-client";

type MediaPlaybackSettings = {
	actionType?: "playback" | "spotify_uri" | "local_file";
	playbackAction?: "play" | "pause" | "next" | "prev" | "toggle";
	playbackPlayer?: "spotify" | "local" | "auto";
	spotifyUri?: string;
	localFilePath?: string;
	localFileVolume?: number;
};

@action({ UUID: "com.streamfloworg.streamflow-sd.mediaplayback" })
export class MediaPlayback extends SingletonAction<MediaPlaybackSettings> {
	private activeActions = new Map<string, { action: any; settings: MediaPlaybackSettings }>();

	constructor() {
		super();

		// Subscribe to real-time media broadcasts
		streamFlowClient.onMediaState((state) => {
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
			}
		});
	}

	override onWillAppear(ev: WillAppearEvent<MediaPlaybackSettings>): void {
		this.activeActions.set(ev.action.id, { action: ev.action, settings: ev.payload.settings });

		if (streamFlowClient.getConnectionState() !== "connected") {
			ev.action.setTitle(streamFlowClient.getConnectionState() === "connecting" ? "Connecting" : "Offline");
		} else {
			this.updateActionUI(ev.action, ev.payload.settings, null);
		}
	}

	override onWillDisappear(ev: WillDisappearEvent<MediaPlaybackSettings>): void {
		this.activeActions.delete(ev.action.id);
	}

	override async onKeyDown(ev: KeyDownEvent<MediaPlaybackSettings>): Promise<void> {
		if (streamFlowClient.getConnectionState() !== "connected") {
			ev.action.setTitle("Connecting...");
			return;
		}

		const settings = ev.payload.settings;
		const actionType = settings.actionType ?? "playback";

		if (actionType === "spotify_uri") {
			if (settings.spotifyUri) {
				await streamFlowClient.playSpotifyUri(settings.spotifyUri);
			} else {
				ev.action.setTitle("No URI");
			}
		} else if (actionType === "local_file") {
			if (settings.localFilePath) {
				const vol = settings.localFileVolume ?? 100;
				await streamFlowClient.playLocalFile(settings.localFilePath, vol);
			} else {
				ev.action.setTitle("No File");
			}
		} else {
			const action = settings.playbackAction ?? "toggle";
			const player = settings.playbackPlayer ?? "auto";
			await streamFlowClient.controlMedia(action, player);
		}
	}

	private updateActionUI(action: any, settings: MediaPlaybackSettings, state: MediaState | null): void {
		const actionType = settings.actionType ?? "playback";

		if (actionType === "spotify_uri") {
			action.setTitle("Play\nSpotify");
		} else if (actionType === "local_file") {
			let filename = "Sound";
			if (settings.localFilePath) {
				const parts = settings.localFilePath.split(/[\\/]/);
				filename = parts[parts.length - 1];
				// truncate if too long
				if (filename.length > 8) {
					filename = filename.substring(0, 7) + "..";
				}
			}
			action.setTitle(`SFX\n${filename}`);
		} else {
			const pAction = settings.playbackAction ?? "toggle";
			if (pAction === "next") {
				action.setTitle("Skip\n▶▶");
			} else if (pAction === "prev") {
				action.setTitle("Prev\n◀◀");
			} else if (pAction === "play") {
				action.setTitle("Play\n▶");
			} else if (pAction === "pause") {
				action.setTitle("Pause\n❚❚");
			} else {
				// toggle play/pause
				if (state) {
					if (state.playing) {
						let trackName = state.title || "Song";
						if (trackName.length > 8) {
							trackName = trackName.substring(0, 7) + "..";
						}
						action.setTitle(`Pause\n${trackName}`);
					} else {
						action.setTitle("Play\n❚❚");
					}
				} else {
					action.setTitle("Play/\nPause");
				}
			}
		}
	}
}
