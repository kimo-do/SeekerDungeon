export const FEED_EVENT_TYPES = [
  "chest_opened",
  "chest_loot",
  "boss_defeated",
  "extracted",
  "duel_won"
] as const;

export type FeedEventType = (typeof FEED_EVENT_TYPES)[number];

export type FeedPublishPayload = {
  type: FeedEventType;
  actorDisplayName: string;
  targetDisplayName?: string;
  itemName?: string;
  itemRarity?: string;
  roomLabel?: string;
  unixTime?: number;
  clientEventId?: string;
};

export type FeedEvent = {
  id: number;
  type: FeedEventType;
  message: string;
  createdAtUnix: number;
};

type FeedSubscriber = (event: FeedEvent) => void;

const CONTROL_CHARS_REGEX = /[\u0000-\u001f\u007f]/g;
const WHITESPACE_REGEX = /\s+/g;
const ITEM_VOWEL_REGEX = /^[aeiou]/i;
const MAX_ACTOR_NAME_LENGTH = 64;
const MAX_TARGET_NAME_LENGTH = 64;
const MAX_ITEM_NAME_LENGTH = 96;
const MAX_ITEM_RARITY_LENGTH = 24;
const MAX_ROOM_LABEL_LENGTH = 48;
const MAX_CLIENT_EVENT_ID_LENGTH = 64;

const sanitizeText = (value: unknown, maxLength: number): string | null => {
  if (typeof value !== "string") {
    return null;
  }

  const cleaned = value
    .replace(CONTROL_CHARS_REGEX, "")
    .replace(WHITESPACE_REGEX, " ")
    .trim();
  if (!cleaned) {
    return null;
  }

  if (cleaned.length <= maxLength) {
    return cleaned;
  }

  return cleaned.slice(0, maxLength).trim();
};

const toUnixSeconds = (value: unknown): number | null => {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return null;
  }

  const rounded = Math.floor(value);
  if (rounded <= 0) {
    return null;
  }

  return rounded;
};

const capitalize = (value: string): string => {
  if (!value) {
    return value;
  }

  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase();
};

const formatLootMessage = (actor: string, itemName: string | null, itemRarity: string | null): string => {
  if (!itemName) {
    return `${actor} received loot`;
  }

  if (itemRarity) {
    return `${actor} received ${capitalize(itemRarity)} ${itemName}`;
  }

  const article = ITEM_VOWEL_REGEX.test(itemName) ? "an" : "a";
  return `${actor} received ${article} ${itemName}`;
};

const formatFeedMessage = (payload: FeedPublishPayload): string => {
  const actor = payload.actorDisplayName;
  switch (payload.type) {
    case "chest_opened":
      return `${actor} opened a chest`;
    case "chest_loot":
      return formatLootMessage(actor, payload.itemName ?? null, payload.itemRarity ?? null);
    case "boss_defeated":
      return `${actor} defeated a boss`;
    case "extracted":
      return `${actor} extracted`;
    case "duel_won":
      if (payload.targetDisplayName) {
        return `${actor} defeated ${payload.targetDisplayName} in a duel`;
      }
      return `${actor} won a duel`;
    default:
      return `${actor} did something impressive`;
  }
};

const isFeedEventType = (value: unknown): value is FeedEventType => {
  return typeof value === "string" && FEED_EVENT_TYPES.includes(value as FeedEventType);
};

export const parseFeedPublishPayload = (body: unknown): FeedPublishPayload => {
  if (!body || typeof body !== "object") {
    throw new Error("Invalid payload.");
  }

  const raw = body as Record<string, unknown>;
  if (!isFeedEventType(raw.type)) {
    throw new Error("Invalid feed event type.");
  }

  const actorDisplayName = sanitizeText(raw.actorDisplayName, MAX_ACTOR_NAME_LENGTH);
  if (!actorDisplayName) {
    throw new Error("Missing actorDisplayName.");
  }

  const payload: FeedPublishPayload = {
    type: raw.type,
    actorDisplayName
  };

  const targetDisplayName = sanitizeText(raw.targetDisplayName, MAX_TARGET_NAME_LENGTH);
  if (targetDisplayName) {
    payload.targetDisplayName = targetDisplayName;
  }

  const itemName = sanitizeText(raw.itemName, MAX_ITEM_NAME_LENGTH);
  if (itemName) {
    payload.itemName = itemName;
  }

  const itemRarity = sanitizeText(raw.itemRarity, MAX_ITEM_RARITY_LENGTH);
  if (itemRarity) {
    payload.itemRarity = itemRarity;
  }

  const roomLabel = sanitizeText(raw.roomLabel, MAX_ROOM_LABEL_LENGTH);
  if (roomLabel) {
    payload.roomLabel = roomLabel;
  }

  const unixTime = toUnixSeconds(raw.unixTime);
  if (unixTime !== null) {
    payload.unixTime = unixTime;
  }

  const clientEventId = sanitizeText(raw.clientEventId, MAX_CLIENT_EVENT_ID_LENGTH);
  if (clientEventId) {
    payload.clientEventId = clientEventId;
  }

  return payload;
};

export class FeedService {
  private readonly maxEvents: number;
  private nextId = 1;
  private readonly events: Array<FeedEvent> = [];
  private readonly subscribers: Set<FeedSubscriber> = new Set();

  public constructor(maxEvents: number) {
    this.maxEvents = Math.max(1, Math.floor(maxEvents));
  }

  public publish(payload: FeedPublishPayload): FeedEvent {
    const event: FeedEvent = {
      id: this.nextId,
      type: payload.type,
      message: formatFeedMessage(payload),
      createdAtUnix: payload.unixTime ?? Math.floor(Date.now() / 1000)
    };

    this.nextId += 1;
    this.events.push(event);
    const overflow = this.events.length - this.maxEvents;
    if (overflow > 0) {
      this.events.splice(0, overflow);
    }

    for (const subscriber of this.subscribers) {
      try {
        subscriber(event);
      } catch {
        // Ignore a failed subscriber callback; do not impact feed publishing.
      }
    }

    return event;
  }

  public listAfter(afterId: number, limit: number): Array<FeedEvent> {
    if (limit <= 0) {
      return [];
    }

    return this.events.filter((entry) => entry.id > afterId).slice(0, limit);
  }

  public subscribe(subscriber: FeedSubscriber): () => void {
    this.subscribers.add(subscriber);
    return () => {
      this.subscribers.delete(subscriber);
    };
  }
}
