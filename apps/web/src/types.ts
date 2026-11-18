export type Activity = {
  id: string; title: string; description: string | null; placeName: string | null;
  latitude: number | null; longitude: number | null; durationMinutes: number | null;
  startTime: string | null; endTime: string | null;
  estimatedCost: number | null; currencyCode: string | null; sortOrder: number;
};
export type ItineraryDay = { id: string; dayNumber: number; date: string; activities: Activity[] };
export type Itinerary = {
  id: string; tripId: string; version: number; isApproved: boolean;
  approvedAt: string | null; days: ItineraryDay[];
};
