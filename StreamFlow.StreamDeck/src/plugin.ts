import streamDeck from "@elgato/streamdeck";

import { StreamControl } from "./actions/stream-control";
import { AudioControl } from "./actions/audio-control";
import { MediaPlayback } from "./actions/media-playback";
import { SceneControl } from "./actions/scene-control";

// We can enable "trace" logging so that all messages between the Stream Deck, and the plugin are recorded. When storing sensitive information
streamDeck.logger.setLevel("trace");

// Register the actions.
streamDeck.actions.registerAction(new StreamControl());
streamDeck.actions.registerAction(new AudioControl());
streamDeck.actions.registerAction(new MediaPlayback());
streamDeck.actions.registerAction(new SceneControl());

// Finally, connect to the Stream Deck.
streamDeck.connect();
