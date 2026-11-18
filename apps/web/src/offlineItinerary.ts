import type { Itinerary } from "./types";

const DB_NAME = "travelos-offline";
const STORE_NAME = "itineraries";
const DB_VERSION = 1;

export type OfflineUser = { id:string; email:string; displayName:string; roles:string[] };
export type OfflineTrip = { id:string; name:string; destination:string; startDate:string; endDate:string; currencyCode:string; ownerUserId:string; currentUserRole:string; memberCount:number; createdAt:string };
export type OfflineTripIndex = { user:OfflineUser; trips:OfflineTrip[]; cachedAt:string; schemaVersion:number };

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    if (typeof indexedDB === "undefined") {
      reject(new Error("Offline storage is not available in this browser."));
      return;
    }
    const request = indexedDB.open(DB_NAME, DB_VERSION);
    request.onupgradeneeded = () => {
      const database = request.result;
      if (!database.objectStoreNames.contains(STORE_NAME)) {
        database.createObjectStore(STORE_NAME, { keyPath: "tripId" });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("Could not open offline storage."));
  });
}

export async function saveOfflineItinerary(tripId: string, itinerary: Itinerary | null, tripName: string, destination: string) {
  const database = await openDatabase();
  try {
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(STORE_NAME, "readwrite");
      transaction.objectStore(STORE_NAME).put({
        tripId, itinerary, tripName, destination,
        cachedAt: new Date().toISOString(),
        schemaVersion: 1
      });
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error ?? new Error("Could not save the offline itinerary."));
      transaction.onabort = () => reject(transaction.error ?? new Error("Offline save was cancelled."));
    });
  } finally {
    database.close();
  }
}

export async function readOfflineItinerary(tripId: string) {
  const database = await openDatabase();
  try {
    return await new Promise<{
      tripId: string; itinerary: Itinerary | null; tripName: string;
      destination: string; cachedAt: string; schemaVersion: number;
    } | null>((resolve, reject) => {
      const request = database.transaction(STORE_NAME, "readonly").objectStore(STORE_NAME).get(tripId);
      request.onsuccess = () => resolve(request.result ?? null);
      request.onerror = () => reject(request.error ?? new Error("Could not read the offline itinerary."));
    });
  } finally {
    database.close();
  }
}

export async function removeOfflineItinerary(tripId: string) {
  const database = await openDatabase();
  try {
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(STORE_NAME, "readwrite");
      transaction.objectStore(STORE_NAME).delete(tripId);
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error ?? new Error("Could not remove the offline itinerary."));
    });
  } finally {
    database.close();
  }
}

export async function saveOfflineTripIndex(user: OfflineUser, trips: OfflineTrip[]) {
  const database = await openDatabase();
  try {
    await new Promise<void>((resolve, reject) => {
      const transaction = database.transaction(STORE_NAME, "readwrite");
      transaction.objectStore(STORE_NAME).put({
        tripId: "__travelos_trip_index__", user, trips,
        cachedAt: new Date().toISOString(), schemaVersion: 1
      });
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error ?? new Error("Could not save the offline trip list."));
    });
  } finally { database.close(); }
}

export async function readOfflineTripIndex(): Promise<OfflineTripIndex | null> {
  const database = await openDatabase();
  try {
    return await new Promise((resolve, reject) => {
      const request = database.transaction(STORE_NAME, "readonly").objectStore(STORE_NAME).get("__travelos_trip_index__");
      request.onsuccess = () => resolve(request.result ?? null);
      request.onerror = () => reject(request.error ?? new Error("Could not read the offline trip list."));
    });
  } finally { database.close(); }
}
