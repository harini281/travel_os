import { FormEvent, useCallback, useEffect, useState } from "react";
import ItineraryMap from "./ItineraryMap";
import { readOfflineItinerary, readOfflineTripIndex, removeOfflineItinerary, saveOfflineItinerary, saveOfflineTripIndex } from "./offlineItinerary";

const API_URL = import.meta.env.VITE_PLATFORM_API_URL ?? "http://localhost:8080";

type AuthUser = { id: string; email: string; displayName: string; roles: string[] };
type AuthResponse = { accessToken: string; tokenType: string; expiresAt: string; user: AuthUser };
type Trip = {
  id: string; name: string; destination: string; startDate: string; endDate: string;
  currencyCode: string; ownerUserId: string; currentUserRole: string; memberCount: number;
  createdAt: string;
};
type Activity = {
  id: string; title: string; description: string | null; placeName: string | null;
  latitude: number | null; longitude: number | null; durationMinutes: number | null;
  startTime: string | null; endTime: string | null;
  estimatedCost: number | null; currencyCode: string | null; sortOrder: number;
};
type ItineraryDay = { id: string; dayNumber: number; date: string; activities: Activity[] };
type Itinerary = {
  id: string; tripId: string; version: number; isApproved: boolean;
  approvedAt: string | null; days: ItineraryDay[];
};
type WeatherAssessment = {
  id:string; activityId:string; activityTitle:string; placeName:string|null; activityDate:string;
  weatherCode:number; precipitationProbabilityMax:number|null; precipitationSumMm:number|null;
  windSpeedMaxKmh:number|null; temperatureMaxC:number|null; severity:"low"|"moderate"|"high";
  summary:string; recommendation:string; source:string; forecastFetchedAt:string;
};
type WeatherSkippedActivity = {activityId:string;activityTitle:string;activityDate:string;reason:string};
type WeatherScanResponse = {
  tripId:string; scannedAt:string; status:string; assessments:WeatherAssessment[];
  skippedActivities:WeatherSkippedActivity[]; highSeverityCount:number;
  moderateSeverityCount:number; message:string;
};
type TripFlight = {
  id:string; tripId:string; flightIdentifier:string; journeyGroup:string; originAirportCode:string; destinationAirportCode:string;
  scheduledDepartureUtc:string; scheduledArrivalUtc:string; status:string; provider:string;
  providerFlightId:string|null; providerStatus:string|null; estimatedDepartureUtc:string|null;
  actualDepartureUtc:string|null; estimatedArrivalUtc:string|null; actualArrivalUtc:string|null;
  departureDelayMinutes:number|null; lastCheckedAt:string|null; createdAt:string; updatedAt:string;
};
type FlightConnection = {
  inboundFlightId:string; journeyGroup:string; inboundFlightIdentifier:string;
  inboundOriginAirportCode:string; inboundDestinationAirportCode:string;
  inboundScheduledArrivalUtc:string; inboundEffectiveArrivalUtc:string; inboundStatus:string;
  outboundFlightId:string; outboundFlightIdentifier:string; outboundOriginAirportCode:string;
  outboundDestinationAirportCode:string; outboundScheduledDepartureUtc:string;
  outboundEffectiveDepartureUtc:string; outboundStatus:string; gapMinutes:number;
  sameAirport:boolean; riskLevel:string; heuristicBufferMinutes:number|null;
  timeSource:string; explanation:string;
};
type GroundTransferEstimate = {
  airportIataCode:string; airportName:string; airportLatitude:number; airportLongitude:number;
  destination:string; destinationLatitude:number; destinationLongitude:number; travelMode:string;
  distanceKm:number; roadDurationMinutes:number; pickupBufferMinutes:number; totalPlanningMinutes:number;
  arrivalUtc:string|null; recommendedDepartureUtc:string|null; provider:string; estimatedAt:string; disclaimer:string;
};
type OpeningHoursActivity = { activityId:string; activityTitle:string; placeName:string|null; activityDate:string; startTime:string|null; endTime:string|null; status:string; matchedPlace:string|null; publishedOpeningHours:string|null; recommendation:string; };
type OpeningHoursDay = { dayId:string; dayNumber:number; date:string; activities:OpeningHoursActivity[]; };
type OpeningHoursAnalysis = { tripId:string; checkedAt:string; days:OpeningHoursDay[]; disclaimer:string; };
type ActivityRouteLeg = { fromActivityId:string; fromTitle:string; toActivityId:string; toTitle:string; gapMinutes:number; distanceKm:number|null; roadDurationMinutes:number|null; requiredMinutes:number|null; status:"feasible"|"tight"|"schedule_conflict"|"unavailable"; explanation:string; };
type RouteDayAnalysis = { dayId:string; dayNumber:number; date:string; legs:ActivityRouteLeg[]; message:string|null; };
type RouteAnalysis = { tripId:string; travelMode:string; bufferMinutes:number; days:RouteDayAnalysis[]; disclaimer:string; };
type JourneyTimelineItem = { id:string; kind:"flight"|"activity"; sortAt:number; dateKey:string; title:string; subtitle:string; timeLabel:string; status:string; };
type ScheduleTransition = { fromActivityId:string; fromTitle:string; toActivityId:string; toTitle:string; gapMinutes:number; status:"overlap"|"short_gap"|"buffer_met"; recommendation:string; };
type ScheduleSuggestion = { activityId:string; activityTitle:string; originalStartTime:string; originalEndTime:string; suggestedStartTime:string|null; suggestedEndTime:string|null; shiftMinutes:number|null; status:"suggested"|"cannot_fit"; reason:string; };
type ScheduleDayAnalysis = { dayId:string; dayNumber:number; date:string; activityCount:number; timedActivityCount:number; unscheduledActivityCount:number; overlapCount:number; shortGapCount:number; transitions:ScheduleTransition[]; suggestions:ScheduleSuggestion[]; };
type ScheduleAnalysis = { tripId:string; minimumGapMinutes:number; days:ScheduleDayAnalysis[]; disclaimer:string; };
type FlightConnectionsResponse = {
  tripId:string; assessedAt:string; connections:FlightConnection[]; disclaimer:string;
};
type FlightRefreshResponse = {
  flight:TripFlight; hasDisruptionSignal:boolean; departureDateActivityCount:number;
  arrivalDateActivityCount:number; itineraryImpactSummary:string;
};
type SuggestedActivity = Omit<Activity, "id">;
type AiDraftDay = { dayNumber: number; date: string; theme: string; activities: SuggestedActivity[] };
type AiDraft = {
  id: string; tripId: string; status: string; createdAt: string;
  provider: string; model: string | null; warnings: string[];
  itinerary: { title: string; summary: string; days: AiDraftDay[] };
};
type BudgetItem = { id: string; category: string; amount: number; currencyCode: string };
type Budget = {
  id: string; tripId: string; amount: number; currencyCode: string;
  items: BudgetItem[]; totalsByCurrency: { currencyCode: string; plannedAmount: number }[];
};
type TripMember = { userId: string; displayName: string; email: string; role: string; joinedAt: string };
type TripInvitation = { id: string; tripId: string; tripName: string; destination: string; invitedByName: string; createdAt: string; expiresAt: string };
type TripInvitationRecord = { id: string; tripId: string; tripName: string; destination: string; invitedByName: string; status: string; createdAt: string; expiresAt: string };
type MyTripPreference = { id: string; tripId: string; userId: string; interests: string[]; pace: string; travelStyle: string; dailyBudgetAmount: number | null; dailyBudgetCurrencyCode: string | null; notes: string | null; updatedAt: string };
type GroupTripPreference = { userId: string; displayName: string; interests: string[]; pace: string; travelStyle: string; dailyBudgetAmount: number | null; dailyBudgetCurrencyCode: string | null; updatedAt: string };
const preferenceInterestOptions = ["culture","history","nature","adventure","food","shopping","photography","nightlife","beaches","art","wellness","local experiences"];
type GroupPollOption = { id: string; label: string; voteCount: number; isSelected: boolean };
type GroupPoll = { id: string; tripId: string; title: string; description: string | null; status: string; createdByUserId: string; createdByName: string; createdAt: string; closesAt: string | null; totalVotes: number; myVoteOptionId: string | null; options: GroupPollOption[] };
type TripExpenseShare = { userId: string; displayName: string; shareAmount: number };
type TripExpense = { id: string; tripId: string; title: string; category: string; notes: string | null; amount: number; currencyCode: string; expenseDate: string; paidByUserId: string; paidByName: string; createdByUserId: string; createdAt: string; shares: TripExpenseShare[] };
type SettlementBalance = { userId: string; displayName: string; netAmount: number };
type SettlementTransfer = { fromUserId: string; fromName: string; toUserId: string; toName: string; amount: number };
type CurrencySettlement = { currencyCode: string; totalExpenses: number; balances: SettlementBalance[]; suggestedTransfers: SettlementTransfer[] };
type TripSettlement = { tripId: string; expenseCount: number; currencies: CurrencySettlement[] };
type TripRepayment = { id: string; tripId: string; fromUserId: string; fromName: string; toUserId: string; toName: string; amount: number; currencyCode: string; status: "pending" | "confirmed" | "rejected"; note: string | null; createdAt: string; respondedAt: string | null };
type TripExpenseAudit = { id: string; expenseId: string; action: "created" | "updated" | "deleted"; changedByUserId: string; changedByName: string; changedAt: string; beforeJson: string; afterJson: string };
type TripReceipt = { id:string; tripId:string; expenseId:string|null; uploadedByUserId:string; originalFileName:string; contentType:string; fileSizeBytes:number; sha256:string; extractionStatus:"pending_review"|"needs_review"|"confirmed"; merchantName:string|null; receiptDate:string|null; totalAmount:number|null; currencyCode:string|null; confidence:number|null; extractionWarning:string|null; uploadedAt:string; confirmedAt:string|null; confirmedByUserId:string|null; isDuplicate:boolean };
type ReceiptDraft = { merchantName:string; receiptDate:string; totalAmount:string; currencyCode:string; expenseId:string };
type ExpenseIntelligence = {
  tripId:string; generatedAt:string; expenseCount:number; receiptCount:number;
  actualsByCurrency:{currencyCode:string;totalAmount:number;expenseCount:number;categories:{category:string;totalAmount:number;expenseCount:number}[]}[];
  forecast:{budgetAmount:number|null;currencyCode:string|null;actualAmount:number|null;remainingAmount:number|null;averageDailySpend:number|null;projectedTotal:number|null;projectedVariance:number|null;status:string;explanation:string;elapsedDays:number;remainingDays:number};
  plannedCategoryTotals:{currencyCode:string;category:string;plannedAmount:number}[];
  categoryComparisons:{currencyCode:string;category:string;plannedAmount:number;actualAmount:number}[];
  receiptMismatches:{receiptId:string;expenseId:string;fileName:string;receiptAmount:number;receiptCurrency:string|null;expenseAmount:number;expenseCurrency:string;status:string;explanation:string}[];
  duplicateExpenseCandidates:{expenseDate:string;amount:number;currencyCode:string;expenses:{expenseId:string;title:string;category:string}[];explanation:string}[];
};

type BudgetIntelligence = {
  tripId:string; generatedAt:string; reportingCurrencyCode:string;
  budgetCurrencyCode:string|null; budgetAmount:number|null; budgetComparable:boolean;
  convertedActualTotal:number; remainingBudget:number|null;
  averageDailySpend:number|null; projectedTotal:number|null; status:string; explanation:string;
  convertedExpenses:{expenseId:string;title:string;category:string;originalAmount:number;originalCurrencyCode:string;convertedAmount:number;reportingCurrencyCode:string;exchangeRate:number;rateDate:string;provider:string;isConverted:boolean}[];
  unconvertedExpenses:{expenseId:string;title:string;category:string;originalAmount:number;currencyCode:string;expenseDate:string;reason:string}[];
  unconvertedByCurrency:{currencyCode:string;originalAmount:number;expenseCount:number}[];
  categoryTotals:{category:string;actualAmount:number;expenseCount:number;plannedAmount:number}[];
  plannedBudgetCategories:{category:string;plannedAmount:number}[];
  expenseCount:number; convertedExpenseCount:number; unconvertedExpenseCount:number;
  elapsedDays:number; remainingDays:number;
};
type BudgetAlert = {
  id:string; alertType:string; thresholdPercent:number; budgetAmountSnapshot:number;
  currencyCode:string; actualAmountSnapshot:number; projectedAmountSnapshot:number|null;
  message:string; triggeredAt:string; isRead:boolean;
};
type BudgetAlertEvaluation = {
  tripId:string; budgetId:string; currencyCode:string; budgetAmount:number;
  actualAmount:number; projectedAmount:number|null; status:string;
  unconvertedExpenseCount:number; newAlertsCreated:number; message:string;
};
type CurrencyOption = { isoCode:string; name:string; symbol:string|null };
type CurrencyConversion = { id:string; expenseId:string; originalAmount:number; originalCurrencyCode:string; convertedAmount:number; convertedCurrencyCode:string; exchangeRate:number; rateDate:string; provider:string; createdAt:string };
type ApiError = { message?: string; title?: string; errors?: Record<string, string[]> };

async function api<T>(path: string, token?: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  });
  if (!response.ok) {
    const payload = await response.json().catch(() => ({})) as ApiError;
    const validation = payload.errors ? Object.values(payload.errors).flat().join(" ") : "";
    throw new Error(validation || payload.message || payload.title || `Request failed (${response.status})`);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

function formatDate(value: string) {
  return new Date(`${value}T12:00:00`).toLocaleDateString(undefined, {
    day: "numeric", month: "short", year: "numeric",
  });
}
function formatFlightTime(value: string, timeZone: string) {
  try { return new Intl.DateTimeFormat(undefined, { timeZone, dateStyle: "medium", timeStyle: "short", timeZoneName: "short" }).format(new Date(value)); }
  catch { return new Date(value).toLocaleString(); }
}
function isValidTimeZone(value: string) {
  try { new Intl.DateTimeFormat("en", { timeZone: value }); return true; } catch { return false; }
}
function initials(name: string) {
  return name.trim().split(/\s+/).slice(0, 2).map(part => part[0]?.toUpperCase()).join("");
}
function dayNumberFor(date: string, tripStart: string) {
  const selected = new Date(`${date}T00:00:00Z`).getTime();
  const start = new Date(`${tripStart}T00:00:00Z`).getTime();
  return Math.floor((selected - start) / 86400000) + 1;
}

export default function App() {
  const [token, setToken] = useState<string | null>(() => sessionStorage.getItem("travelos.accessToken"));
  const [user, setUser] = useState<AuthUser | null>(null);
  const [trips, setTrips] = useState<Trip[]>([]);
  const [authMode, setAuthMode] = useState<"login" | "register">("login");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [destination, setDestination] = useState("");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [currencyCode, setCurrencyCode] = useState("USD");

  const [selectedTrip, setSelectedTrip] = useState<Trip | null>(null);
  const [displayTimeZone, setDisplayTimeZone] = useState(() => {
    const saved = localStorage.getItem("travelos.displayTimeZone");
    return saved && isValidTimeZone(saved) ? saved : Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  });
  const [isOnline, setIsOnline] = useState(navigator.onLine);
  const [offlineCachedAt, setOfflineCachedAt] = useState<string | null>(null);
  const [isOfflineSnapshot, setIsOfflineSnapshot] = useState(false);
  const [offlineBusy, setOfflineBusy] = useState(false);

  const [itinerary, setItinerary] = useState<Itinerary | null>(null);
  const [scheduleAnalysis, setScheduleAnalysis] = useState<ScheduleAnalysis | null>(null);
  const [routeAnalysis, setRouteAnalysis] = useState<RouteAnalysis | null>(null);
  const [openingHoursAnalysis, setOpeningHoursAnalysis] = useState<OpeningHoursAnalysis | null>(null);
  const [loadingOpeningHours, setLoadingOpeningHours] = useState(false);
  const [routeBuffer, setRouteBuffer] = useState("15");
  const [loadingRouteAnalysis, setLoadingRouteAnalysis] = useState(false);
  const [scheduleBuffer, setScheduleBuffer] = useState("30");
  const [loadingScheduleAnalysis, setLoadingScheduleAnalysis] = useState(false);
  const [applyingScheduleSuggestionId, setApplyingScheduleSuggestionId] = useState<string | null>(null);
  const [loadingItinerary, setLoadingItinerary] = useState(false);
  const [weatherAssessments, setWeatherAssessments] = useState<WeatherAssessment[]>([]);
  const [weatherSkipped, setWeatherSkipped] = useState<WeatherSkippedActivity[]>([]);
  const [weatherScanStatus, setWeatherScanStatus] = useState("");
  const [weatherScannedAt, setWeatherScannedAt] = useState<string | null>(null);
  const [loadingWeather, setLoadingWeather] = useState(false);
  const [tripFlights, setTripFlights] = useState<TripFlight[]>([]);
  const [transferAirport, setTransferAirport] = useState("CMB");
  const [transferDestination, setTransferDestination] = useState("");
  const [transferMode, setTransferMode] = useState("driving");
  const [transferBuffer, setTransferBuffer] = useState("30");
  const [transferArrival, setTransferArrival] = useState("");
  const [transferEstimate, setTransferEstimate] = useState<GroundTransferEstimate | null>(null);
  const [estimatingTransfer, setEstimatingTransfer] = useState(false);
  const [flightConnections, setFlightConnections] = useState<FlightConnection[]>([]);
  const [connectionDisclaimer, setConnectionDisclaimer] = useState("");
  const [loadingConnections, setLoadingConnections] = useState(false);
  const [journeyGroup, setJourneyGroup] = useState("Outbound");
  const [loadingFlights, setLoadingFlights] = useState(false);
  const [refreshingFlightId, setRefreshingFlightId] = useState<string | null>(null);
  const [showAddFlight, setShowAddFlight] = useState(false);
  const [flightIdentifier, setFlightIdentifier] = useState("");
  const [flightOrigin, setFlightOrigin] = useState("");
  const [flightDestination, setFlightDestination] = useState("");
  const [flightDeparture, setFlightDeparture] = useState("");
  const [flightArrival, setFlightArrival] = useState("");
  const [showAddDay, setShowAddDay] = useState(false);
  const [newDayDate, setNewDayDate] = useState("");
  const [activeDayId, setActiveDayId] = useState<string | null>(null);
  const [editingActivityId, setEditingActivityId] = useState<string | null>(null);
  const [activityTitle, setActivityTitle] = useState("");
  const [activityPlace, setActivityPlace] = useState("");
  const [activityDescription, setActivityDescription] = useState("");
  const [activityDuration, setActivityDuration] = useState("");
  const [activityStartTime, setActivityStartTime] = useState("");
  const [activityEndTime, setActivityEndTime] = useState("");
  const [activityCost, setActivityCost] = useState("");
  const [aiDrafts, setAiDrafts] = useState<AiDraft[]>([]);
  const [travellerCount, setTravellerCount] = useState(1);
  const [interestsText, setInterestsText] = useState("");
  const [travelPace, setTravelPace] = useState<"relaxed" | "balanced" | "fast">("balanced");
  const [budget, setBudget] = useState<Budget | null>(null);
  const [budgetReportingCurrency, setBudgetReportingCurrency] = useState("USD");
  const [budgetIntelligence, setBudgetIntelligence] = useState<BudgetIntelligence | null>(null);
  const [loadingBudgetIntelligence, setLoadingBudgetIntelligence] = useState(false);
  const [budgetAlerts, setBudgetAlerts] = useState<BudgetAlert[]>([]);
  const [loadingBudgetAlerts, setLoadingBudgetAlerts] = useState(false);
  const [evaluatingBudgetAlerts, setEvaluatingBudgetAlerts] = useState(false);
  const [budgetAmount, setBudgetAmount] = useState("");
  const [budgetCurrency, setBudgetCurrency] = useState("USD");
  const [budgetCategory, setBudgetCategory] = useState("");
  const [budgetItemAmount, setBudgetItemAmount] = useState("");
  const [budgetItemCurrency, setBudgetItemCurrency] = useState("");
  const [tripMembers, setTripMembers] = useState<TripMember[]>([]);
  const [tripInvitations, setTripInvitations] = useState<TripInvitationRecord[]>([]);
  const [myInvitations, setMyInvitations] = useState<TripInvitation[]>([]);
  const [inviteEmail, setInviteEmail] = useState("");
  const [myPreference, setMyPreference] = useState<MyTripPreference | null>(null);
  const [groupPreferences, setGroupPreferences] = useState<GroupTripPreference[]>([]);
  const [preferenceInterests, setPreferenceInterests] = useState<string[]>([]);
  const [preferencePace, setPreferencePace] = useState("balanced");
  const [preferenceStyle, setPreferenceStyle] = useState("mixed");
  const [preferenceBudget, setPreferenceBudget] = useState("");
  const [preferenceCurrency, setPreferenceCurrency] = useState("USD");
  const [preferenceNotes, setPreferenceNotes] = useState("");
  const [groupPolls, setGroupPolls] = useState<GroupPoll[]>([]);
  const [pollTitle, setPollTitle] = useState("");
  const [pollDescription, setPollDescription] = useState("");
  const [pollOptions, setPollOptions] = useState(["", ""]);
  const [pollClosesAt, setPollClosesAt] = useState("");
  const [tripExpenses, setTripExpenses] = useState<TripExpense[]>([]);
  const [tripSettlement, setTripSettlement] = useState<TripSettlement | null>(null);
  const [tripRepayments, setTripRepayments] = useState<TripRepayment[]>([]);
  const [repaymentTarget, setRepaymentTarget] = useState<{ toUserId: string; toName: string; fromName: string; currencyCode: string; suggestedAmount: number } | null>(null);
  const [repaymentAmount, setRepaymentAmount] = useState("");
  const [repaymentNote, setRepaymentNote] = useState("");
  const [expenseTitle, setExpenseTitle] = useState("");
  const [expenseCategory, setExpenseCategory] = useState("food");
  const [expenseAmount, setExpenseAmount] = useState("");
  const [expenseCurrency, setExpenseCurrency] = useState("USD");
  const [expenseDate, setExpenseDate] = useState(new Date().toISOString().slice(0,10));
  const [expenseNotes, setExpenseNotes] = useState("");
  const [expensePaidBy, setExpensePaidBy] = useState("");
  const [expenseParticipants, setExpenseParticipants] = useState<string[]>([]);
  const [expenseSplitType, setExpenseSplitType] = useState<"equal" | "percentage" | "exact">("equal");
  const [expenseCustomShares, setExpenseCustomShares] = useState<Record<string, string>>({});
  const [editingExpenseId, setEditingExpenseId] = useState<string | null>(null);
  const [editExpenseDraft, setEditExpenseDraft] = useState<{title:string;category:string;notes:string;amount:string;currencyCode:string;expenseDate:string;paidByUserId:string}>({title:"",category:"food",notes:"",amount:"",currencyCode:"USD",expenseDate:"",paidByUserId:""});
  const [editExpenseShares, setEditExpenseShares] = useState<Record<string,string>>({});
  const [expenseAuditHistory, setExpenseAuditHistory] = useState<Record<string,TripExpenseAudit[]>>({});
  const [expandedExpenseHistory, setExpandedExpenseHistory] = useState<string | null>(null);
  const [tripReceipts, setTripReceipts] = useState<TripReceipt[]>([]);
  const [receiptFile, setReceiptFile] = useState<File | null>(null);
  const [receiptExpenseId, setReceiptExpenseId] = useState("");
  const [receiptDrafts, setReceiptDrafts] = useState<Record<string,ReceiptDraft>>({});
  const [expenseIntelligence, setExpenseIntelligence] = useState<ExpenseIntelligence | null>(null);
  const [loadingExpenseIntelligence, setLoadingExpenseIntelligence] = useState(false);
  const [currencyOptions, setCurrencyOptions] = useState<CurrencyOption[]>([]);
  const [currencyConversions, setCurrencyConversions] = useState<CurrencyConversion[]>([]);
  const [conversionTargetCurrency, setConversionTargetCurrency] = useState("USD");
  const [convertingExpenseId, setConvertingExpenseId] = useState<string | null>(null);

  const loadTrips = useCallback(async (accessToken: string) => {
    const data = await api<Trip[]>("/api/v1/trips", accessToken);
    setTrips(data);
    return data;
  }, []);
  const loadProfile = useCallback(async (accessToken: string) => {
    const profile = await api<AuthUser>("/api/v1/auth/me", accessToken);
    setUser(profile);
    const [tripData] = await Promise.all([
      loadTrips(accessToken),
      api<TripInvitation[]>("/api/v1/me/invitations", accessToken).then(setMyInvitations),
    ]);
    try { await saveOfflineTripIndex(profile, tripData); } catch { /* Offline index is an optional enhancement. */ }
  }, [loadTrips]);

  useEffect(() => {
    const updateOnline = () => setIsOnline(navigator.onLine);
    window.addEventListener("online", updateOnline);
    window.addEventListener("offline", updateOnline);
    return () => { window.removeEventListener("online", updateOnline); window.removeEventListener("offline", updateOnline); };
  }, []);

  useEffect(() => { if (selectedTrip && isValidTimeZone(displayTimeZone)) localStorage.setItem(`travelos.tripTimeZone.${selectedTrip.id}`, displayTimeZone); }, [displayTimeZone, selectedTrip]);

  useEffect(() => {
    if (!token) return;
    loadProfile(token).catch(async err => {
      if (!navigator.onLine) {
        try {
          const cached = await readOfflineTripIndex();
          if (cached) {
            setUser(cached.user);
            setTrips(cached.trips);
            setNotice(`Offline mode · trip list last saved ${new Date(cached.cachedAt).toLocaleString()}.`);
            return;
          }
        } catch { /* Fall through to the offline availability message. */ }
        setError("You're offline and no saved trip list is available on this device yet.");
        return;
      }
      if (String(err instanceof Error ? err.message : err).includes("401")) {
        sessionStorage.removeItem("travelos.accessToken");
        setToken(null); setUser(null); setTrips([]);
        setError("Your session expired. Please sign in again.");
      } else {
        setError("TravelOS cannot reach the server. Your saved offline trips remain available if this device has a cached trip list.");
        try {
          const cached = await readOfflineTripIndex();
          if (cached) { setUser(cached.user); setTrips(cached.trips); }
        } catch { /* No offline index available. */ }
      }
    });
  }, [token, loadProfile]);

  async function loadItinerary(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    setLoadingItinerary(true); setError(""); setIsOfflineSnapshot(false); setOfflineCachedAt(null);
    try {
      const result = await api<Itinerary | undefined>(`/api/v1/trips/${trip.id}/itinerary`, accessToken);
      setItinerary(result ?? null);
      try {
        await saveOfflineItinerary(trip.id, result ?? null, trip.name, trip.destination);
        setOfflineCachedAt(new Date().toISOString());
      } catch { /* Offline saving is optional if browser storage is unavailable. */ }
      const analysis = await api<ScheduleAnalysis>(
        `/api/v1/trips/${trip.id}/itinerary/schedule-analysis?minimumGapMinutes=${scheduleBuffer}`, accessToken,
      );
      setScheduleAnalysis(analysis);
    } catch (err) {
      try {
        const cached = await readOfflineItinerary(trip.id);
        if (cached) {
          setItinerary(cached.itinerary);
          setOfflineCachedAt(cached.cachedAt);
          setIsOfflineSnapshot(true);
          setScheduleAnalysis(null);
          setError("You're viewing a saved offline snapshot. Editing and live analysis are unavailable until you reconnect.");
        } else setError(err instanceof Error ? err.message : "Unable to load itinerary.");
      } catch { setError(err instanceof Error ? err.message : "Unable to load itinerary."); }
    } finally { setLoadingItinerary(false); }
  }

  async function cacheCurrentItinerary() {
    if (!selectedTrip) return;
    setOfflineBusy(true);
    try {
      await saveOfflineItinerary(selectedTrip.id, itinerary, selectedTrip.name, selectedTrip.destination);
      setOfflineCachedAt(new Date().toISOString());
      setNotice("This itinerary is saved on this device for offline viewing.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to save an offline copy."); }
    finally { setOfflineBusy(false); }
  }

  async function clearOfflineItinerary() {
    if (!selectedTrip) return;
    setOfflineBusy(true);
    try {
      await removeOfflineItinerary(selectedTrip.id);
      setOfflineCachedAt(null);
      setNotice("The saved offline copy was removed from this device.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to remove the offline copy."); }
    finally { setOfflineBusy(false); }
  }

  async function applyScheduleSuggestion(suggestion: ScheduleSuggestion) {
    if (!token || !selectedTrip || !itinerary) return;
    if (selectedTrip.currentUserRole !== "owner" || itinerary.isApproved) {
      setError("Only the trip owner can adjust an unapproved itinerary.");
      return;
    }
    const activity = itinerary.days.flatMap(day => day.activities)
      .find(item => item.id === suggestion.activityId);
    if (!activity || suggestion.status !== "suggested" ||
        !suggestion.suggestedStartTime || !suggestion.suggestedEndTime) {
      setError("This schedule suggestion is no longer available. Recheck the schedule.");
      return;
    }
    if (activity.startTime?.slice(0,5) !== suggestion.originalStartTime ||
        activity.endTime?.slice(0,5) !== suggestion.originalEndTime) {
      setError("This activity has changed since the suggestion was generated. Recheck the schedule before applying it.");
      await refreshScheduleAnalysis();
      return;
    }

    setApplyingScheduleSuggestionId(suggestion.activityId);
    setError("");
    try {
      await api<Activity>(`/api/v1/trips/${selectedTrip.id}/itinerary/activities/${activity.id}`, token, {
        method:"PUT",
        body:JSON.stringify({
          title:activity.title, description:activity.description,
          placeName:activity.placeName, latitude:activity.latitude, longitude:activity.longitude,
          durationMinutes:activity.durationMinutes,
          startTime:suggestion.suggestedStartTime + ":00",
          endTime:suggestion.suggestedEndTime + ":00",
          estimatedCost:activity.estimatedCost, currencyCode:activity.currencyCode,
          sortOrder:activity.sortOrder
        })
      });
      await loadItinerary(selectedTrip);
      setNotice(`Applied the suggested time for ${activity.title}. Review the updated schedule.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to apply this schedule suggestion.");
    } finally {
      setApplyingScheduleSuggestionId(null);
    }
  }

  async function refreshScheduleAnalysis() {
    if (!token || !selectedTrip) return;
    setLoadingScheduleAnalysis(true);
    try {
      const analysis = await api<ScheduleAnalysis>(
        `/api/v1/trips/${selectedTrip.id}/itinerary/schedule-analysis?minimumGapMinutes=${scheduleBuffer}`, token,
      );
      setScheduleAnalysis(analysis);
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to analyse the daily schedule."); }
    finally { setLoadingScheduleAnalysis(false); }
  }

  async function loadTripFlights(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    setLoadingFlights(true);
    try {
      const data = await api<TripFlight[]>(`/api/v1/trips/${trip.id}/flights`, accessToken);
      setTripFlights(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load saved flights.");
    } finally { setLoadingFlights(false); }
  }

  async function loadFlightConnections(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    setLoadingConnections(true);
    try {
      const data = await api<FlightConnectionsResponse>(`/api/v1/trips/${trip.id}/flights/connections`, accessToken);
      setFlightConnections(data.connections);
      setConnectionDisclaimer(data.disclaimer);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to assess flight connections.");
    } finally { setLoadingConnections(false); }
  }

  async function refreshFlightConnections() {
    if (!token || !selectedTrip) return;
    await loadFlightConnections(selectedTrip);
  }

  async function addTripFlight(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      const created = await api<TripFlight>(`/api/v1/trips/${selectedTrip.id}/flights`, token, {
        method:"POST",
        body:JSON.stringify({
          flightIdentifier, originAirportCode:flightOrigin, destinationAirportCode:flightDestination,
          journeyGroup, scheduledDepartureUtc:new Date(flightDeparture).toISOString(),
          scheduledArrivalUtc:new Date(flightArrival).toISOString()
        })
      });
      setTripFlights(items => [...items, created].sort((a,b) => a.scheduledDepartureUtc.localeCompare(b.scheduledDepartureUtc)));
      setFlightIdentifier(""); setFlightOrigin(""); setFlightDestination(""); setFlightDeparture(""); setFlightArrival("");
      await loadFlightConnections(selectedTrip);
      setShowAddFlight(false); setNotice("Flight saved. Live status is not yet confirmed.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to save flight details.");
    } finally { setBusy(false); }
  }

  async function refreshFlightStatus(flight: TripFlight) {
    if (!token || !selectedTrip) return;
    setRefreshingFlightId(flight.id); setError("");
    try {
      const result = await api<FlightRefreshResponse>(
        `/api/v1/trips/${selectedTrip.id}/flights/${flight.id}/refresh`, token, {method:"POST"}
      );
      setTripFlights(items => items.map(item => item.id === flight.id ? result.flight : item));
      await loadFlightConnections(selectedTrip);
      setNotice(result.itineraryImpactSummary);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to refresh flight status.");
    } finally { setRefreshingFlightId(null); }
  }

  async function analyzeOpeningHours() {
    if (!token || !selectedTrip) return;
    setLoadingOpeningHours(true); setError(""); setOpeningHoursAnalysis(null);
    try {
      const result = await api<OpeningHoursAnalysis>(
        `/api/v1/trips/${selectedTrip.id}/itinerary/opening-hours-analysis`, token
      );
      setOpeningHoursAnalysis(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to check attraction opening hours.");
    } finally { setLoadingOpeningHours(false); }
  }

  async function analyzeItineraryRoutes() {
    if (!token || !selectedTrip) return;
    setLoadingRouteAnalysis(true); setError(""); setRouteAnalysis(null);
    try {
      const result = await api<RouteAnalysis>(
        `/api/v1/trips/${selectedTrip.id}/itinerary/route-analysis?bufferMinutes=${routeBuffer}`, token
      );
      setRouteAnalysis(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to analyse itinerary road routes.");
    } finally { setLoadingRouteAnalysis(false); }
  }

  async function estimateGroundTransfer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setEstimatingTransfer(true); setError(""); setTransferEstimate(null);
    try {
      const result = await api<GroundTransferEstimate>(
        `/api/v1/trips/${selectedTrip.id}/ground-transfers/estimate`, token, {
          method:"POST", body:JSON.stringify({
            airportIataCode:transferAirport, destination:transferDestination, travelMode:transferMode,
            pickupBufferMinutes:Number(transferBuffer), arrivalUtc:transferArrival ? new Date(transferArrival).toISOString() : null
          })
        }
      );
      setTransferEstimate(result);
      setNotice("Route estimate ready. No transfer has been booked.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to estimate the airport transfer.");
    } finally { setEstimatingTransfer(false); }
  }

  async function loadWeatherAssessments(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const data = await api<WeatherAssessment[]>(`/api/v1/trips/${trip.id}/weather-disruptions`, accessToken);
      setWeatherAssessments(data);
      setWeatherScannedAt(data.reduce<string | null>((latest, item) =>
        !latest || item.forecastFetchedAt > latest ? item.forecastFetchedAt : latest, null));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load weather assessments.");
    }
  }

  async function scanTripWeather() {
    if (!token || !selectedTrip) return;
    setLoadingWeather(true); setError("");
    try {
      const result = await api<WeatherScanResponse>(
        `/api/v1/trips/${selectedTrip.id}/weather-disruptions/scan`, token, { method:"POST" }
      );
      setWeatherAssessments(result.assessments);
      setWeatherSkipped(result.skippedActivities);
      setWeatherScanStatus(result.status);
      setWeatherScannedAt(result.scannedAt);
      setNotice(result.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to scan trip weather.");
    } finally { setLoadingWeather(false); }
  }

  async function loadDrafts(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const data = await api<AiDraft[]>(`/api/v1/trips/${trip.id}/itinerary/drafts`, accessToken);
      setAiDrafts(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load AI drafts.");
    }
  }

  async function loadBudget(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const result = await api<Budget | undefined>(`/api/v1/trips/${trip.id}/budget`, accessToken);
      setBudget(result ?? null);
      setBudgetAmount(result ? String(result.amount) : "");
      setBudgetCurrency(result?.currencyCode ?? trip.currencyCode);
      const reportingCode = result?.currencyCode ?? trip.currencyCode;
      setBudgetReportingCurrency(reportingCode);
      await loadBudgetIntelligence(trip, reportingCode, accessToken);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load trip budget.");
    }
  }

  async function loadBudgetAlerts(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    setLoadingBudgetAlerts(true);
    try {
      const data = await api<BudgetAlert[]>(`/api/v1/trips/${trip.id}/budget-alerts`, accessToken);
      setBudgetAlerts(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load budget alerts.");
    } finally { setLoadingBudgetAlerts(false); }
  }

  async function evaluateBudgetAlerts() {
    if (!token || !selectedTrip) return;
    setEvaluatingBudgetAlerts(true); setError("");
    try {
      const result = await api<BudgetAlertEvaluation>(
        `/api/v1/trips/${selectedTrip.id}/budget-alerts/evaluate`, token, { method:"POST" }
      );
      await loadBudgetAlerts(selectedTrip);
      setNotice(result.unconvertedExpenseCount > 0
        ? result.message
        : result.newAlertsCreated > 0
          ? `${result.newAlertsCreated} new budget alert(s) created for your trip group.`
          : result.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to evaluate budget alerts.");
    } finally { setEvaluatingBudgetAlerts(false); }
  }

  async function markBudgetAlertRead(alertId: string) {
    if (!token || !selectedTrip) return;
    try {
      await api<void>(`/api/v1/trips/${selectedTrip.id}/budget-alerts/${alertId}/read`, token, { method:"POST" });
      setBudgetAlerts(current => current.map(alert => alert.id === alertId ? {...alert,isRead:true} : alert));
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to mark alert as read."); }
  }

  async function refreshBudgetAlerts(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      await api<BudgetAlertEvaluation>(`/api/v1/trips/${trip.id}/budget-alerts/evaluate`, accessToken, { method:"POST" });
      await loadBudgetAlerts(trip, accessToken);
    } catch {
      // A trip without a configured budget has no thresholds to evaluate.
      // The explicit Check budget action surfaces evaluation errors to the user.
    }
  }

  async function loadBudgetIntelligence(trip: Trip, reportingCurrency = budgetReportingCurrency, accessToken = token) {
    if (!accessToken) return;
    setLoadingBudgetIntelligence(true);
    try {
      const data = await api<BudgetIntelligence>(
        `/api/v1/trips/${trip.id}/budget-intelligence?reportingCurrencyCode=${encodeURIComponent(reportingCurrency)}`,
        accessToken
      );
      setBudgetIntelligence(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load multi-currency budget intelligence.");
    } finally { setLoadingBudgetIntelligence(false); }
  }

  async function loadGroupMembers(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const members = await api<TripMember[]>(`/api/v1/trips/${trip.id}/members`, accessToken);
      setTripMembers(members);
      setExpenseParticipants(members.map(member => member.userId));
      setExpensePaidBy(current => current || members.find(member => member.role === "owner")?.userId || members[0]?.userId || "");
      if (trip.currentUserRole === "owner") {
        const invitations = await api<TripInvitationRecord[]>(`/api/v1/trips/${trip.id}/invitations`, accessToken);
        setTripInvitations(invitations);
      } else setTripInvitations([]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load group members.");
    }
  }




  async function loadTripExpenses(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const data = await api<TripExpense[]>(`/api/v1/trips/${trip.id}/expenses`, accessToken);
      setTripExpenses(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load shared expenses.");
    }
  }




  async function loadExpenseIntelligence(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    setLoadingExpenseIntelligence(true);
    try {
      const data = await api<ExpenseIntelligence>(`/api/v1/trips/${trip.id}/expense-intelligence`, accessToken);
      setExpenseIntelligence(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load expense intelligence.");
    } finally { setLoadingExpenseIntelligence(false); }
  }

  async function loadCurrencyIntelligence(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const snapshots = await api<CurrencyConversion[]>("/api/v1/trips/" + trip.id + "/currency-conversions", accessToken);
      setCurrencyConversions(snapshots);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load saved conversion history.");
    }
    try {
      const currencies = await api<CurrencyOption[]>("/api/v1/trips/" + trip.id + "/currency-conversions/currencies", accessToken);
      setCurrencyOptions(currencies);
      setConversionTargetCurrency(current =>
        currencies.some(item => item.isoCode === current) ? current : trip.currencyCode);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load supported currencies.");
    }
  }

  async function convertExpenseCurrency(expense: TripExpense) {
    if (!token || !selectedTrip) return;
    setConvertingExpenseId(expense.id);
    setError("");
    try {
      const snapshot = await api<CurrencyConversion>(
        `/api/v1/trips/${selectedTrip.id}/currency-conversions`, token,
        { method: "POST", body: JSON.stringify({
          expenseId: expense.id, targetCurrencyCode: conversionTargetCurrency
        }) }
      );
      setCurrencyConversions(current => [snapshot, ...current]);
      await refreshBudgetAlerts(selectedTrip);
      setNotice(`Reference conversion saved: ${snapshot.originalAmount.toLocaleString()} ${snapshot.originalCurrencyCode} = ${snapshot.convertedAmount.toLocaleString()} ${snapshot.convertedCurrencyCode}.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to convert this expense.");
    } finally { setConvertingExpenseId(null); }
  }

  async function loadTripReceipts(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const data = await api<TripReceipt[]>(`/api/v1/trips/${trip.id}/receipts`, accessToken);
      setTripReceipts(data);
      setReceiptDrafts(Object.fromEntries(data.map(receipt => [receipt.id, {
        merchantName: receipt.merchantName ?? "",
        receiptDate: receipt.receiptDate ?? "",
        totalAmount: receipt.totalAmount === null ? "" : String(receipt.totalAmount),
        currencyCode: receipt.currencyCode ?? "",
        expenseId: receipt.expenseId ?? ""
      }])));
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to load receipts."); }
  }

  async function uploadTripReceipt(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip || !receiptFile) return;
    const body = new FormData();
    body.append("file", receiptFile);
    if (receiptExpenseId) body.append("expenseId", receiptExpenseId);
    setBusy(true); setError("");
    try {
      const response = await fetch(`${API_URL}/api/v1/trips/${selectedTrip.id}/receipts`, {
        method: "POST", headers: { Authorization: `Bearer ${token}` }, body
      });
      if (!response.ok) {
        const payload = await response.json().catch(() => ({}));
        throw new Error(payload.message ?? payload.title ?? `Upload failed (${response.status})`);
      }
      setReceiptFile(null); setReceiptExpenseId("");
      await Promise.all([loadTripReceipts(selectedTrip), loadExpenseIntelligence(selectedTrip)]);
      setNotice("Receipt uploaded. Review the extracted details before confirming.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to upload receipt."); }
    finally { setBusy(false); }
  }

  async function confirmTripReceipt(receipt: TripReceipt) {
    if (!token || !selectedTrip) return;
    const draft = receiptDrafts[receipt.id];
    if (!draft) return;
    setBusy(true); setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/receipts/${receipt.id}/confirm`, token, {
        method: "POST",
        body: JSON.stringify({
          merchantName: draft.merchantName.trim() || null,
          receiptDate: draft.receiptDate || null,
          totalAmount: draft.totalAmount === "" ? null : Number(draft.totalAmount),
          currencyCode: draft.currencyCode.trim().toUpperCase() || null,
          expenseId: draft.expenseId || null
        })
      });
      await Promise.all([loadTripReceipts(selectedTrip), loadExpenseIntelligence(selectedTrip)]);
      setNotice("Receipt details confirmed.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to confirm receipt."); }
    finally { setBusy(false); }
  }

  async function deleteTripReceipt(receipt: TripReceipt) {
    if (!token || !selectedTrip || !window.confirm(`Delete receipt "${receipt.originalFileName}"?`)) return;
    try {
      await api<void>(`/api/v1/trips/${selectedTrip.id}/receipts/${receipt.id}`, token, { method: "DELETE" });
      await Promise.all([loadTripReceipts(selectedTrip), loadExpenseIntelligence(selectedTrip)]);
      setNotice("Receipt deleted.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to delete receipt."); }
  }

  async function loadTripSettlement(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const data = await api<TripSettlement>(`/api/v1/trips/${trip.id}/settlements`, accessToken);
      setTripSettlement(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to calculate settlement suggestions.");
    }
  }


  async function loadTripRepayments(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const data = await api<TripRepayment[]>(`/api/v1/trips/${trip.id}/repayments`, accessToken);
      setTripRepayments(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load repayment records.");
    }
  }

  function beginRepayment(transfer: SettlementTransfer, currencyCode: string) {
    if (!user || transfer.fromUserId !== user.id) return;
    setRepaymentTarget({
      toUserId: transfer.toUserId, toName: transfer.toName,
      fromName: transfer.fromName, currencyCode, suggestedAmount: transfer.amount
    });
    setRepaymentAmount(transfer.amount.toFixed(2));
    setRepaymentNote("");
  }

  async function submitRepayment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip || !repaymentTarget) return;
    setBusy(true); setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/repayments`, token, {
        method: "POST",
        body: JSON.stringify({
          toUserId: repaymentTarget.toUserId,
          amount: Number(repaymentAmount),
          currencyCode: repaymentTarget.currencyCode,
          note: repaymentNote.trim() || null
        })
      });
      setRepaymentTarget(null); setRepaymentAmount(""); setRepaymentNote("");
      await Promise.all([loadTripRepayments(selectedTrip), loadTripSettlement(selectedTrip)]);
      setNotice("Repayment submitted. The recipient must confirm receipt before balances change.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to submit repayment."); }
    finally { setBusy(false); }
  }

  async function respondToRepayment(repayment: TripRepayment, decision: "confirm" | "reject") {
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/repayments/${repayment.id}/respond`, token, {
        method: "POST", body: JSON.stringify({ decision })
      });
      await Promise.all([loadTripRepayments(selectedTrip), loadTripSettlement(selectedTrip)]);
      setNotice(decision === "confirm"
        ? "Repayment confirmed. Outstanding balances have been updated."
        : "Repayment rejected. Outstanding balances remain unchanged.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to update repayment."); }
    finally { setBusy(false); }
  }

  async function createTripExpense(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip || !expensePaidBy) return;
    setBusy(true); setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/expenses`, token, {
        method: "POST",
        body: JSON.stringify({
          title: expenseTitle, category: expenseCategory, notes: expenseNotes.trim() || null,
          amount: Number(expenseAmount), currencyCode: expenseCurrency, expenseDate,
          paidByUserId: expensePaidBy, participantUserIds: expenseParticipants,
          splitType: expenseSplitType,
          customShares: expenseSplitType === "equal" ? [] : expenseParticipants.map(userId => ({
            userId, value: Number(expenseCustomShares[userId] || 0)
          })),
        }),
      });
      setExpenseTitle(""); setExpenseAmount(""); setExpenseNotes("");
      setExpenseSplitType("equal"); setExpenseCustomShares({});
      await Promise.all([loadTripExpenses(selectedTrip), loadTripSettlement(selectedTrip), loadExpenseIntelligence(selectedTrip)]);
      await refreshBudgetAlerts(selectedTrip);
      setNotice(`Shared expense added with ${expenseSplitType} splitting.`);
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to add shared expense."); }
    finally { setBusy(false); }
  }


  function beginEditExpense(expense: TripExpense) {
    setEditingExpenseId(expense.id);
    setEditExpenseDraft({
      title: expense.title, category: expense.category, notes: expense.notes ?? "",
      amount: String(expense.amount), currencyCode: expense.currencyCode,
      expenseDate: expense.expenseDate, paidByUserId: expense.paidByUserId
    });
    setEditExpenseShares(Object.fromEntries(expense.shares.map(s => [s.userId, String(s.shareAmount)])));
  }

  async function saveExpenseEdit(event: FormEvent<HTMLFormElement>, expense: TripExpense) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    const participants = Object.keys(editExpenseShares);
    setBusy(true); setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/expenses/${expense.id}`, token, {
        method: "PUT",
        body: JSON.stringify({
          ...editExpenseDraft, amount: Number(editExpenseDraft.amount),
          participantUserIds: participants, splitType: "exact",
          customShares: participants.map(userId => ({userId,value:Number(editExpenseShares[userId])}))
        })
      });
      setEditingExpenseId(null);
      await Promise.all([loadTripExpenses(selectedTrip),loadTripSettlement(selectedTrip),loadExpenseIntelligence(selectedTrip)]);
      await refreshBudgetAlerts(selectedTrip);
      setNotice("Expense updated. Settlement balances have been recalculated and the change was recorded.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to update expense."); }
    finally { setBusy(false); }
  }

  async function loadExpenseHistory(expense: TripExpense) {
    if (!token || !selectedTrip) return;
    if (expandedExpenseHistory === expense.id) { setExpandedExpenseHistory(null); return; }
    setExpandedExpenseHistory(expense.id);
    try {
      const history = await api<TripExpenseAudit[]>(`/api/v1/trips/${selectedTrip.id}/expenses/${expense.id}/history`, token);
      setExpenseAuditHistory(current => ({...current,[expense.id]:history}));
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to load expense history."); }
  }

  async function deleteTripExpense(expense: TripExpense) {
    if (!token || !selectedTrip || !window.confirm(`Remove expense "${expense.title}"?`)) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${selectedTrip.id}/expenses/${expense.id}`, token, { method: "DELETE" });
      await Promise.all([loadTripExpenses(selectedTrip), loadTripSettlement(selectedTrip), loadExpenseIntelligence(selectedTrip)]);
      await refreshBudgetAlerts(selectedTrip);
      setNotice("Expense removed.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to remove expense."); }
  }

  function defaultCustomShareValues(type: "percentage" | "exact", amount: string, ids: string[]) {
    const result: Record<string, string> = {};
    if (!ids.length) return result;
    const total = type === "percentage" ? 100 : Number(amount || 0);
    const unit = Math.floor((total / ids.length) * 10000) / 10000;
    ids.forEach((id, index) => {
      const value = index === 0 ? total - unit * (ids.length - 1) : unit;
      result[id] = value.toFixed(4);
    });
    return result;
  }

  function changeExpenseSplitType(type: "equal" | "percentage" | "exact") {
    setExpenseSplitType(type);
    setExpenseCustomShares(type === "equal" ? {} : defaultCustomShareValues(type, expenseAmount, expenseParticipants));
  }

  function toggleExpenseParticipant(userId: string) {
    const next = expenseParticipants.includes(userId)
      ? expenseParticipants.filter(id => id !== userId)
      : [...expenseParticipants, userId];
    setExpenseParticipants(next);
    if (expenseSplitType !== "equal")
      setExpenseCustomShares(defaultCustomShareValues(expenseSplitType, expenseAmount, next));
  }

  async function loadGroupPolls(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const data = await api<GroupPoll[]>(`/api/v1/trips/${trip.id}/polls`, accessToken);
      setGroupPolls(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load group polls.");
    }
  }

  async function createGroupPoll(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/polls`, token, {
        method: "POST",
        body: JSON.stringify({
          title: pollTitle, description: pollDescription.trim() || null,
          options: pollOptions.map(option => option.trim()).filter(Boolean),
          closesAt: pollClosesAt ? new Date(pollClosesAt).toISOString() : null,
        }),
      });
      setPollTitle(""); setPollDescription(""); setPollOptions(["", ""]); setPollClosesAt("");
      await loadGroupPolls(selectedTrip);
      setNotice("Your group poll has been created.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to create group poll."); }
    finally { setBusy(false); }
  }

  async function castGroupVote(poll: GroupPoll, optionId: string) {
    if (!token || !selectedTrip || poll.status !== "open") return;
    setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/polls/${poll.id}/vote`, token, {
        method: "POST", body: JSON.stringify({ optionId }),
      });
      await loadGroupPolls(selectedTrip);
      setNotice("Your vote has been recorded.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to record vote."); }
  }

  async function closeGroupPoll(poll: GroupPoll) {
    if (!token || !selectedTrip || !window.confirm("Close this poll? Members will no longer be able to change their votes.")) return;
    setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/polls/${poll.id}/close`, token, { method: "POST" });
      await loadGroupPolls(selectedTrip);
      setNotice("Poll closed. Results are final.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to close poll."); }
  }

  function updatePollOption(index: number, value: string) {
    setPollOptions(current => current.map((option, optionIndex) => optionIndex === index ? value : option));
  }

  async function loadPreferences(trip: Trip, accessToken = token) {
    if (!accessToken) return;
    try {
      const [mine, group] = await Promise.all([
        api<MyTripPreference | undefined>(`/api/v1/trips/${trip.id}/preferences/me`, accessToken),
        api<GroupTripPreference[]>(`/api/v1/trips/${trip.id}/preferences/group`, accessToken),
      ]);
      setMyPreference(mine ?? null);
      setGroupPreferences(group);
      setPreferenceInterests(mine?.interests ?? []);
      setPreferencePace(mine?.pace ?? "balanced");
      setPreferenceStyle(mine?.travelStyle ?? "mixed");
      setPreferenceBudget(mine?.dailyBudgetAmount == null ? "" : String(mine.dailyBudgetAmount));
      setPreferenceCurrency(mine?.dailyBudgetCurrencyCode ?? trip.currencyCode);
      setPreferenceNotes(mine?.notes ?? "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to load traveller preferences.");
    }
  }

  async function savePreferences(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      const saved = await api<MyTripPreference>(`/api/v1/trips/${selectedTrip.id}/preferences/me`, token, {
        method: "PUT",
        body: JSON.stringify({
          interests: preferenceInterests, pace: preferencePace, travelStyle: preferenceStyle,
          dailyBudgetAmount: preferenceBudget.trim() ? Number(preferenceBudget) : null,
          dailyBudgetCurrencyCode: preferenceBudget.trim() ? preferenceCurrency : null,
          notes: preferenceNotes.trim() || null,
        }),
      });
      setMyPreference(saved);
      const group = await api<GroupTripPreference[]>(`/api/v1/trips/${selectedTrip.id}/preferences/group`, token);
      setGroupPreferences(group);
      setNotice("Your trip preferences have been saved.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to save preferences."); }
    finally { setBusy(false); }
  }

  function togglePreferenceInterest(interest: string) {
    setPreferenceInterests(current => current.includes(interest)
      ? current.filter(item => item !== interest)
      : current.length < 8 ? [...current, interest] : current);
  }

  async function loadMyInvitations(accessToken = token) {
    if (!accessToken) return;
    const invitations = await api<TripInvitation[]>("/api/v1/me/invitations", accessToken);
    setMyInvitations(invitations);
  }

  async function openTrip(trip: Trip) {
    const savedZone = localStorage.getItem(`travelos.tripTimeZone.${trip.id}`);
    setDisplayTimeZone(savedZone && isValidTimeZone(savedZone) ? savedZone : Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC");
    setSelectedTrip(trip); setItinerary(null); setAiDrafts([]); setWeatherAssessments([]); setWeatherSkipped([]); setWeatherScanStatus(""); setWeatherScannedAt(null); setTripFlights([]); setFlightConnections([]); setConnectionDisclaimer(""); setShowAddFlight(false); setBudget(null); setBudgetIntelligence(null); setBudgetReportingCurrency(trip.currencyCode); setBudgetAlerts([]);
    setTripMembers([]); setTripInvitations([]); setMyPreference(null); setGroupPreferences([]); setGroupPolls([]); setTripExpenses([]); setTripSettlement(null); setTripRepayments([]); setTripReceipts([]); setReceiptDrafts({}); setExpenseIntelligence(null); setBudgetIntelligence(null); setCurrencyConversions([]); setCurrencyOptions([]); setConversionTargetCurrency(trip.currencyCode); setRepaymentTarget(null);
    setExpensePaidBy(""); setExpenseParticipants([]);
    setShowAddDay(false); setActiveDayId(null);
    if (!navigator.onLine) {
      await loadItinerary(trip);
      return;
    }
    await Promise.all([loadItinerary(trip), loadDrafts(trip), loadWeatherAssessments(trip), loadTripFlights(trip), loadFlightConnections(trip), loadBudget(trip), loadBudgetAlerts(trip), loadGroupMembers(trip), loadPreferences(trip), loadGroupPolls(trip), loadTripExpenses(trip), loadTripSettlement(trip), loadTripRepayments(trip), loadTripReceipts(trip), loadExpenseIntelligence(trip), loadCurrencyIntelligence(trip)]);
  }

  function closeTrip() {
    setSelectedTrip(null); setItinerary(null); setAiDrafts([]); setBudget(null);
    setTripMembers([]); setTripInvitations([]); setMyPreference(null); setGroupPreferences([]); setGroupPolls([]); setTripExpenses([]); setTripSettlement(null); setTripReceipts([]); setReceiptDrafts({}); setExpenseIntelligence(null); setCurrencyConversions([]); setCurrencyOptions([]);
    setShowAddDay(false); setActiveDayId(null); setError("");
  }

  async function handleAuth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setBusy(true); setError("");
    try {
      const payload = authMode === "register"
        ? { email: String(form.get("email")), displayName: String(form.get("displayName")), password: String(form.get("password")) }
        : { email: String(form.get("email")), password: String(form.get("password")) };
      const result = await api<AuthResponse>(
        `/api/v1/auth/${authMode === "register" ? "register" : "login"}`,
        undefined, { method: "POST", body: JSON.stringify(payload) },
      );
      sessionStorage.setItem("travelos.accessToken", result.accessToken);
      setToken(result.accessToken); setUser(result.user);
      setNotice(authMode === "register" ? "Your TravelOS account is ready." : "Welcome back.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to sign in.");
    } finally { setBusy(false); }
  }

  function signOut() {
    sessionStorage.removeItem("travelos.accessToken");
    setToken(null); setUser(null); setTrips([]); setSelectedTrip(null);
    setNotice("You have signed out."); setError("");
  }

  async function createTrip(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!token) return;
    setBusy(true); setError("");
    try {
      const trip = await api<Trip>("/api/v1/trips", token, {
        method: "POST", body: JSON.stringify({ name, destination, startDate, endDate, currencyCode }),
      });
      setTrips(current => [...current, trip].sort((a, b) => a.startDate.localeCompare(b.startDate)));
      setName(""); setDestination(""); setStartDate(""); setEndDate("");
      setCurrencyCode("USD"); setShowCreate(false); setNotice("Your trip has been created.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to create trip.");
    } finally { setBusy(false); }
  }

  async function deleteTrip(trip: Trip) {
    if (!token || !window.confirm(`Delete "${trip.name}"? This cannot be undone.`)) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${trip.id}`, token, { method: "DELETE" });
      setTrips(current => current.filter(item => item.id !== trip.id));
      setNotice("Trip deleted.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to delete trip."); }
  }

  async function createItinerary() {
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api<Itinerary>(`/api/v1/trips/${selectedTrip.id}/itinerary`, token, { method: "POST" });
      await loadItinerary(selectedTrip);
      setNotice("Your itinerary workspace is ready.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to create itinerary."); }
    finally { setBusy(false); }
  }

  async function addDay(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    const dayNumber = dayNumberFor(newDayDate, selectedTrip.startDate);
    setBusy(true); setError("");
    try {
      await api<ItineraryDay>(`/api/v1/trips/${selectedTrip.id}/itinerary/days`, token, {
        method: "POST", body: JSON.stringify({ dayNumber, date: newDayDate }),
      });
      await loadItinerary(selectedTrip);
      setNewDayDate(""); setShowAddDay(false); setNotice("Day added to your itinerary.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to add day."); }
    finally { setBusy(false); }
  }

  async function addActivity(event: FormEvent<HTMLFormElement>, dayId: string) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api<Activity>(
        `/api/v1/trips/${selectedTrip.id}/itinerary/days/${dayId}/activities`, token,
        { method: "POST", body: JSON.stringify({
          title: activityTitle, description: activityDescription || null,
          placeName: activityPlace || null, latitude: null, longitude: null,
          durationMinutes: activityDuration ? Number(activityDuration) : null,
          startTime: activityStartTime ? activityStartTime + ":00" : null,
          endTime: activityEndTime ? activityEndTime + ":00" : null,
          estimatedCost: activityCost ? Number(activityCost) : null,
          currencyCode: activityCost ? selectedTrip.currencyCode : null, sortOrder: 0,
        }) },
      );
      await loadItinerary(selectedTrip);
      setActivityTitle(""); setActivityPlace(""); setActivityDescription("");
      setActivityDuration(""); setActivityStartTime(""); setActivityEndTime(""); setActivityCost(""); setActiveDayId(null);
      setNotice("Activity added.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to add activity."); }
    finally { setBusy(false); }
  }

  function beginEditActivity(activity: Activity) {
    setEditingActivityId(activity.id);
    setActivityTitle(activity.title); setActivityPlace(activity.placeName ?? "");
    setActivityDescription(activity.description ?? "");
    setActivityDuration(activity.durationMinutes?.toString() ?? "");
    setActivityStartTime(activity.startTime?.slice(0,5) ?? "");
    setActivityEndTime(activity.endTime?.slice(0,5) ?? "");
    setActivityCost(activity.estimatedCost?.toString() ?? "");
  }

  async function updateActivity(event: FormEvent<HTMLFormElement>, activityId: string) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api<Activity>(`/api/v1/trips/${selectedTrip.id}/itinerary/activities/${activityId}`, token, {
        method:"PUT", body:JSON.stringify({
          title:activityTitle, description:activityDescription || null,
          placeName:activityPlace || null, latitude:null, longitude:null,
          durationMinutes:activityDuration ? Number(activityDuration) : null,
          startTime:activityStartTime ? activityStartTime + ":00" : null,
          endTime:activityEndTime ? activityEndTime + ":00" : null,
          estimatedCost:activityCost ? Number(activityCost) : null,
          currencyCode:activityCost ? selectedTrip.currencyCode : null, sortOrder:0
        })
      });
      await loadItinerary(selectedTrip);
      setEditingActivityId(null); setActivityTitle(""); setActivityPlace("");
      setActivityDescription(""); setActivityDuration(""); setActivityStartTime("");
      setActivityEndTime(""); setActivityCost(""); setNotice("Activity updated.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to update activity."); }
    finally { setBusy(false); }
  }

  async function deleteActivity(activityId: string) {
    if (!token || !selectedTrip || !window.confirm("Remove this activity from the itinerary?")) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${selectedTrip.id}/itinerary/activities/${activityId}`, token, { method: "DELETE" });
      await loadItinerary(selectedTrip); setNotice("Activity removed.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to remove activity."); }
  }

  async function approveItinerary() {
    if (!token || !selectedTrip) return;
    if (!window.confirm("Approve this itinerary? It will become read-only.")) return;
    setBusy(true); setError("");
    try {
      const approved = await api<Itinerary>(`/api/v1/trips/${selectedTrip.id}/itinerary/approve`, token, { method: "POST" });
      setItinerary(approved); setNotice("Itinerary approved.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to approve itinerary."); }
    finally { setBusy(false); }
  }




  async function sendTripInvitation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api(`/api/v1/trips/${selectedTrip.id}/invitations`, token, {
        method: "POST", body: JSON.stringify({ email: inviteEmail }),
      });
      setInviteEmail("");
      await loadGroupMembers(selectedTrip);
      setNotice("Trip invitation created. The traveller can accept it from their TravelOS invitation inbox.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to invite traveller."); }
    finally { setBusy(false); }
  }

  async function acceptTripInvitation(invitation: TripInvitation) {
    if (!token) return;
    setBusy(true); setError("");
    try {
      await api(`/api/v1/trips/${invitation.tripId}/invitations/${invitation.id}/accept`, token, { method: "POST" });
      await Promise.all([loadTrips(token), loadMyInvitations(token)]);
      setNotice(`You joined ${invitation.tripName}.`);
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to accept invitation."); }
    finally { setBusy(false); }
  }

  async function revokeTripInvitation(invitationId: string) {
    if (!token || !selectedTrip) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${selectedTrip.id}/invitations/${invitationId}`, token, { method: "DELETE" });
      await loadGroupMembers(selectedTrip);
      setNotice("Invitation revoked.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to revoke invitation."); }
  }

  async function removeTripMember(member: TripMember) {
    if (!token || !selectedTrip || !window.confirm(`Remove ${member.displayName} from this trip?`)) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${selectedTrip.id}/members/${member.userId}`, token, { method: "DELETE" });
      await Promise.all([loadGroupMembers(selectedTrip), loadTrips(token)]);
      setNotice(`${member.displayName} was removed from the trip.`);
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to remove member."); }
  }

  async function saveBudget(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      const result = await api<Budget>(`/api/v1/trips/${selectedTrip.id}/budget`, token, {
        method: "PUT",
        body: JSON.stringify({ amount: Number(budgetAmount), currencyCode: budgetCurrency }),
      });
      setBudget(result); await refreshBudgetAlerts(selectedTrip); setNotice("Trip budget saved.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to save budget."); }
    finally { setBusy(false); }
  }

  async function addBudgetItem(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      await api<BudgetItem>(`/api/v1/trips/${selectedTrip.id}/budget/items`, token, {
        method: "POST",
        body: JSON.stringify({
          category: budgetCategory, amount: Number(budgetItemAmount),
          currencyCode: budgetItemCurrency || budget?.currencyCode || selectedTrip.currencyCode,
        }),
      });
      const refreshed = await api<Budget>(`/api/v1/trips/${selectedTrip.id}/budget`, token);
      setBudget(refreshed); setBudgetCategory(""); setBudgetItemAmount("");
      setNotice("Planned budget item added.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to add budget item."); }
    finally { setBusy(false); }
  }

  async function deleteBudgetItem(itemId: string) {
    if (!token || !selectedTrip || !window.confirm("Remove this planned budget item?")) return;
    setError("");
    try {
      await api<void>(`/api/v1/trips/${selectedTrip.id}/budget/items/${itemId}`, token, { method: "DELETE" });
      const refreshed = await api<Budget>(`/api/v1/trips/${selectedTrip.id}/budget`, token);
      setBudget(refreshed); setNotice("Budget item removed.");
    } catch (err) { setError(err instanceof Error ? err.message : "Unable to remove budget item."); }
  }

  async function generateAiDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!token || !selectedTrip) return;
    setBusy(true); setError("");
    try {
      const draft = await api<AiDraft>(`/api/v1/trips/${selectedTrip.id}/itinerary/drafts`, token, {
        method: "POST",
        body: JSON.stringify({
          travellerCount,
          budgetAmount: null,
          interests: interestsText.split(",").map(value => value.trim()).filter(Boolean),
          travelPace,
        }),
      });
      setAiDrafts(current => [draft, ...current]);
      setNotice("AI itinerary draft generated. Review it before applying.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "AI itinerary generation is unavailable.");
    } finally { setBusy(false); }
  }

  async function applyAiDraft(draftId: string) {
    if (!token || !selectedTrip) return;
    if (!window.confirm("Apply this AI draft to your trip? You can review and edit it before approving.")) return;
    setBusy(true); setError("");
    try {
      const updated = await api<Itinerary>(
        `/api/v1/trips/${selectedTrip.id}/itinerary/drafts/${draftId}/apply`,
        token, { method: "POST" },
      );
      setItinerary(updated);
      await loadDrafts(selectedTrip);
      setNotice("AI draft applied as a new editable itinerary version. It is not approved.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unable to apply AI draft.");
    } finally { setBusy(false); }
  }

  if (!token || !user) {
    return (
      <main className="auth-layout">
        <section className="auth-story">
          <a className="brand" href="/">travel<span>OS</span></a>
          <div className="story-copy">
            <p className="eyebrow">YOUR JOURNEY, IN ONE PLACE</p>
            <h1>Travel well.<br /><em>Worry less.</em></h1>
            <p>One thoughtful space to plan journeys, bring people together and keep every detail in view.</p>
          </div>
          <div className="story-foot"><span>01 — PLAN</span><span>02 — CONNECT</span><span>03 — EXPLORE</span></div>
        </section>
        <section className="auth-panel">
          <div className="auth-card">
            <p className="eyebrow">WELCOME TO TRAVELOS</p>
            <h2>{authMode === "login" ? "Welcome back" : "Create your account"}</h2>
            <p className="muted">{authMode === "login" ? "Sign in to pick up where your next journey begins." : "Start bringing your travel plans together."}</p>
            <div className="auth-tabs">
              <button className={authMode === "login" ? "active" : ""} onClick={() => { setAuthMode("login"); setError(""); }}>Sign in</button>
              <button className={authMode === "register" ? "active" : ""} onClick={() => { setAuthMode("register"); setError(""); }}>Create account</button>
            </div>
            <form onSubmit={handleAuth} className="form-stack">
              {authMode === "register" && <label>Full name<input name="displayName" placeholder="Your name" minLength={2} maxLength={120} required /></label>}
              <label>Email address<input name="email" type="email" placeholder="you@example.com" autoComplete="email" required /></label>
              <label>Password<input name="password" type="password" placeholder={authMode === "register" ? "At least 10 characters" : "Your password"} minLength={authMode === "register" ? 10 : undefined} autoComplete={authMode === "register" ? "new-password" : "current-password"} required /></label>
              {authMode === "register" && <p className="field-hint">Use at least 10 characters with uppercase, lowercase, a number and a symbol.</p>}
              {error && <p className="alert error">{error}</p>}
              <button className="btn btn-primary btn-wide" disabled={busy}>{busy ? "Please wait…" : authMode === "login" ? "Sign in →" : "Create account →"}</button>
            </form>
            <p className="auth-legal">By continuing, you agree to use TravelOS responsibly. Account verification and password recovery are not available in this preview.</p>
          </div>
        </section>
      </main>
    );
  }

  const journeyTimeline:JourneyTimelineItem[] = [
    ...tripFlights.map(flight => {
      const departure = flight.actualDepartureUtc ?? flight.estimatedDepartureUtc ?? flight.scheduledDepartureUtc;
      const arrival = flight.actualArrivalUtc ?? flight.estimatedArrivalUtc ?? flight.scheduledArrivalUtc;
      const dep = new Date(departure), arr = new Date(arrival);
      return { id:flight.id, kind:"flight" as const, sortAt:dep.getTime(), dateKey:dep.toLocaleDateString(),
        title:`${flight.flightIdentifier} · ${flight.originAirportCode} → ${flight.destinationAirportCode}`,
        subtitle:`${flight.journeyGroup} · Arrives ${arr.toLocaleString()}`,
        timeLabel:`Departs ${dep.toLocaleString()}`, status:flight.status.replaceAll("_"," ") };
    }),
    ...(itinerary?.days.flatMap(day => day.activities.map(activity => ({
      id:activity.id, kind:"activity" as const,
      sortAt:new Date(day.date + "T" + (activity.startTime ?? "12:00:00")).getTime(),
      dateKey:new Date(day.date + "T12:00:00").toLocaleDateString(),
      title:activity.title, subtitle:activity.placeName ?? "Location not specified",
      timeLabel:activity.startTime && activity.endTime ? `${formatDate(day.date)} · ${activity.startTime.slice(0,5)}–${activity.endTime.slice(0,5)}` : `Day ${day.dayNumber} · ${formatDate(day.date)} · Time not specified`,
      status:"planned"
    }))) ?? [])
  ].sort((left,right)=>left.sortAt-right.sortAt);
  const flightActivityDateKeys = new Set(tripFlights.flatMap(flight => {
    const dep=new Date(flight.actualDepartureUtc ?? flight.estimatedDepartureUtc ?? flight.scheduledDepartureUtc);
    const arr=new Date(flight.actualArrivalUtc ?? flight.estimatedArrivalUtc ?? flight.scheduledArrivalUtc);
    return [dep.toLocaleDateString(),arr.toLocaleDateString()];
  }));
  const sameDayActivityCount = journeyTimeline.filter(item=>item.kind==="activity" && flightActivityDateKeys.has(item.dateKey)).length;
  const scheduleConflicts = new Set<string>();
  itinerary?.days.forEach(day => {
    const timed=day.activities.filter(activity=>activity.startTime && activity.endTime);
    for(let left=0;left<timed.length;left++) for(let right=left+1;right<timed.length;right++)
      if(timed[left].endTime!>timed[right].startTime!) { scheduleConflicts.add(timed[left].id); scheduleConflicts.add(timed[right].id); }
  });

  const canEditItinerary = selectedTrip?.currentUserRole === "owner" && !itinerary?.isApproved && !isOfflineSnapshot;

  return (
    <main className="dashboard">
      <aside className="sidebar">
        <a className="brand" href="/">travel<span>OS</span></a>
        <div className="side-label">WORKSPACE</div>
        <button className={selectedTrip ? "side-link" : "side-link selected"} onClick={closeTrip}><span>▦</span> My trips</button>
        <div className="side-note"><span className="side-dot" /> Your travel space, all together.</div>
        <div className="sidebar-bottom">
          <div className="avatar">{initials(user.displayName)}</div>
          <div className="user-meta"><strong>{user.displayName}</strong><span>{user.email}</span></div>
          <button className="icon-button" onClick={signOut} title="Sign out" aria-label="Sign out">↗</button>
        </div>
      </aside>
      <section className="main-panel">
        <header className="topbar">
          <span className="breadcrumb">Workspace <b>/</b> {selectedTrip ? selectedTrip.name : "My trips"}</span>
          <span className="preview-tag">TRAVELOS PREVIEW</span>
        </header>
        <div className={isOfflineSnapshot ? "content-wrap offline-snapshot" : "content-wrap"}>
          {notice && <div className="alert success">{notice}<button onClick={() => setNotice("")}>×</button></div>}
          {error && <div className="alert error">{error}<button onClick={() => setError("")}>×</button></div>}

          {!selectedTrip ? <>
            <section className="welcome-row">
              <div><p className="eyebrow">YOUR TRAVEL SPACE</p><h1>Good to see you, {user.displayName.split(" ")[0]}.</h1><p className="muted">Every great journey starts with a plan. What's next?</p></div>
              <button className="btn btn-primary" onClick={() => { setShowCreate(v => !v); setError(""); }}>＋ Plan a trip</button>
            </section>

            {myInvitations.length > 0 && <section className="invitation-inbox">
              <div className="section-heading"><div><p className="eyebrow">TRAVEL TOGETHER</p><h2>Trip invitations</h2></div><span className="count-label">{myInvitations.length} PENDING</span></div>
              {myInvitations.map(invitation => <article className="invitation-card" key={invitation.id}>
                <div><strong>{invitation.tripName}</strong><span>{invitation.destination} · Invited by {invitation.invitedByName}</span><small>Expires {formatDate(invitation.expiresAt.slice(0,10))}</small></div>
                <button className="btn btn-primary" disabled={busy} onClick={() => acceptTripInvitation(invitation)}>Accept invitation</button>
              </article>)}
            </section>}

            <section className="stats-row">
              <article><span>YOUR TRIPS</span><strong>{trips.length.toString().padStart(2, "0")}</strong><small>Trips in your workspace</small></article>
              <article><span>UPCOMING</span><strong>{trips.filter(t => t.startDate >= new Date().toISOString().slice(0, 10)).length.toString().padStart(2, "0")}</strong><small>Journeys ahead</small></article>
              <article><span>AS A GROUP</span><strong>{trips.filter(t => t.memberCount > 1).length.toString().padStart(2, "0")}</strong><small>Trips with other travellers</small></article>
            </section>
            {showCreate && <section className="create-card">
              <div className="section-heading"><div><p className="eyebrow">NEW JOURNEY</p><h2>Let's plan somewhere.</h2></div><button className="icon-button" onClick={() => setShowCreate(false)} aria-label="Close form">×</button></div>
              <form className="trip-form" onSubmit={createTrip}>
                <label>Trip name<input value={name} onChange={e => setName(e.target.value)} placeholder="A summer to remember" maxLength={160} required /></label>
                <label>Destination<input value={destination} onChange={e => setDestination(e.target.value)} placeholder="e.g. Kyoto, Japan" maxLength={160} required /></label>
                <label>Start date<input type="date" value={startDate} onChange={e => setStartDate(e.target.value)} required /></label>
                <label>End date<input type="date" value={endDate} min={startDate || undefined} onChange={e => setEndDate(e.target.value)} required /></label>
                <label>Currency<select value={currencyCode} onChange={e => setCurrencyCode(e.target.value)}><option value="USD">USD — US Dollar</option><option value="LKR">LKR — Sri Lankan Rupee</option><option value="EUR">EUR — Euro</option><option value="GBP">GBP — Pound Sterling</option><option value="JPY">JPY — Japanese Yen</option><option value="AUD">AUD — Australian Dollar</option><option value="INR">INR — Indian Rupee</option><option value="SGD">SGD — Singapore Dollar</option></select></label>
                <div className="form-actions"><button type="button" className="btn btn-quiet" onClick={() => setShowCreate(false)}>Cancel</button><button className="btn btn-primary" disabled={busy}>{busy ? "Creating…" : "Create trip →"}</button></div>
              </form>
            </section>}
            <div className="section-heading trips-heading"><div><p className="eyebrow">COLLECTION</p><h2>Your journeys</h2></div><span className="count-label">{trips.length} {trips.length === 1 ? "TRIP" : "TRIPS"}</span></div>
            {trips.length === 0 ? <section className="empty-state"><div className="empty-icon">✳</div><h3>Your next story starts here.</h3><p>You haven't created a trip yet. Start with a destination and a few dates — you can shape the details as you go.</p><button className="btn btn-primary" onClick={() => setShowCreate(true)}>Create your first trip →</button></section> :
              <section className="trip-grid">{trips.map(trip => <article className="trip-card" key={trip.id}>
                <div className="trip-card-top"><span className="trip-mark">✳</span><span className={trip.currentUserRole === "owner" ? "role owner" : "role"}>{trip.currentUserRole}</span></div>
                <p className="trip-destination">{trip.destination}</p><h3>{trip.name}</h3>
                <div className="trip-dates"><span>◷</span>{formatDate(trip.startDate)} — {formatDate(trip.endDate)}</div>
                <div className="trip-card-foot"><span>{trip.memberCount} {trip.memberCount === 1 ? "traveller" : "travellers"} · {trip.currencyCode}</span>{trip.currentUserRole === "owner" && <button onClick={() => deleteTrip(trip)} className="delete-link">Delete</button>}</div>
                <button className="open-trip-link" onClick={() => openTrip(trip)}>Open itinerary →</button>
              </article>)}</section>}
          </> : <>
            <button className="back-link" onClick={closeTrip}>← Back to all trips</button>
            <section className="itinerary-hero">
              <div><p className="eyebrow">YOUR JOURNEY · {selectedTrip.currencyCode}</p><h1>{selectedTrip.name}</h1><p className="muted">{selectedTrip.destination} <span>·</span> {formatDate(selectedTrip.startDate)} — {formatDate(selectedTrip.endDate)}</p></div>
              {itinerary && <span className={itinerary.isApproved ? "approval-pill approved" : "approval-pill"}>{itinerary.isApproved ? "✓ APPROVED" : "DRAFT PLAN"}</span>}
            </section>



            <section className="travel-context-panel">
              <div className="section-heading">
                <div><p className="eyebrow">STAGE 36 · TIME & OFFLINE</p><h2>Travel time settings</h2><small>Choose the timezone used to display UTC flight timestamps</small></div>
                <span className={isOnline?"travel-connectivity online":"travel-connectivity offline"}>{isOnline?"● Online":"● Offline"}</span>
              </div>
              <div className="travel-context-grid">
                <label>Flight display time zone
                  <input list="travelos-timezones" value={displayTimeZone} onChange={event=>setDisplayTimeZone(event.target.value)} aria-invalid={!isValidTimeZone(displayTimeZone)} />
                  <datalist id="travelos-timezones">
                    <option value="Asia/Colombo"/><option value="Asia/Kolkata"/><option value="Asia/Dubai"/>
                    <option value="Europe/London"/><option value="Europe/Paris"/><option value="America/New_York"/>
                    <option value="America/Los_Angeles"/><option value="Asia/Singapore"/><option value="Asia/Tokyo"/>
                    <option value="Australia/Sydney"/><option value="UTC"/>
                  </datalist>
                </label>
                <div className="travel-context-actions">
                  <button type="button" className="btn btn-quiet" onClick={cacheCurrentItinerary} disabled={!itinerary || offlineBusy || !isOnline}>{offlineBusy?"Saving…":"Save offline copy"}</button>
                  {offlineCachedAt && <button type="button" className="btn btn-quiet" onClick={clearOfflineItinerary} disabled={offlineBusy}>Remove offline copy</button>}
                </div>
              </div>
              {!isValidTimeZone(displayTimeZone) && <p className="travel-context-warning">Enter a valid IANA timezone, such as Asia/Colombo or Europe/London.</p>}
              <p className="travel-context-note">Flight timestamps are stored in UTC and displayed in the selected timezone. Activity start/end times are local clock values and are not converted because the itinerary currently has no per-day timezone field.</p>
              <p className="travel-context-note">Offline copies are stored in this browser on this device, without application-level encryption. Avoid saving sensitive trip details on shared devices and remove the copy when finished.</p>
              {offlineCachedAt && <p className="travel-context-note">{isOfflineSnapshot?"Viewing cached snapshot":"Offline copy saved"} · {new Date(offlineCachedAt).toLocaleString()}</p>}
              {isOfflineSnapshot && <p className="travel-context-warning">Read-only offline snapshot. Reconnect to refresh live data or make changes.</p>}
            </section>
            {scheduleAnalysis && <section className="schedule-analysis-panel">
              <div className="section-heading">
                <div><p className="eyebrow">STAGES 29–31 · SCHEDULE INTELLIGENCE</p><h2>Daily schedule review & optimization</h2><small>Detect every conflicting activity pair, including nested overlaps, and review short breaks</small></div>
                <div className="schedule-analysis-controls">
                  <label>Minimum planning buffer
                    <select value={scheduleBuffer} onChange={e=>setScheduleBuffer(e.target.value)}>
                      <option value="15">15 minutes</option><option value="30">30 minutes</option><option value="45">45 minutes</option><option value="60">60 minutes</option>
                    </select>
                  </label>
                  <button type="button" className="btn btn-quiet" onClick={refreshScheduleAnalysis} disabled={loadingScheduleAnalysis}>{loadingScheduleAnalysis?"Checking…":"Recheck schedule"}</button>
                </div>
              </div>
              <div className="schedule-analysis-days">
                {scheduleAnalysis.days.length===0 ? <p className="budget-empty">Add an itinerary to review daily schedules.</p> :
                  scheduleAnalysis.days.map(day=><article className="schedule-analysis-day" key={day.dayId}>
                    <div className="schedule-analysis-day-heading"><strong>Day {day.dayNumber} · {formatDate(day.date)}</strong><span>{day.timedActivityCount}/{day.activityCount} timed</span></div>
                    {day.unscheduledActivityCount>0 && <p className="schedule-analysis-note">{day.unscheduledActivityCount} activities have no start/end time and cannot be checked for gaps.</p>}
                    {day.overlapCount>0 && <p className="schedule-analysis-alert">{day.overlapCount} overlapping activity pair(s) detected.</p>}
                    {day.transitions.map((item,index)=><div className="schedule-transition" key={item.fromActivityId+item.toActivityId+index}>
                      <div><strong>{item.fromTitle} → {item.toTitle}</strong><small>{item.gapMinutes<0?Math.abs(item.gapMinutes)+" min overlap":item.gapMinutes+" min break"}</small></div>
                      <span className={"schedule-transition-status "+item.status}>{item.status.replace("_"," ")}</span>
                      <p>{item.recommendation}</p>
                    </div>)}
                    {day.suggestions.length>0 && <div className="schedule-suggestions">
                      <strong>Suggested time adjustments</strong>
                      <p className="schedule-analysis-note">Proposals only — your saved itinerary has not been changed.</p>
                      {day.suggestions.map(item=><div className="schedule-suggestion" key={item.activityId}>
                        <div><strong>{item.activityTitle}</strong><small>{item.originalStartTime}–{item.originalEndTime}</small></div>
                        {item.status==="suggested" ? <span className="schedule-transition-status buffer_met">{item.suggestedStartTime}–{item.suggestedEndTime} · +{item.shiftMinutes} min</span> : <span className="schedule-transition-status overlap">Needs manual review</span>}
                        <p>{item.reason}</p>
                        {item.status==="suggested" && canEditItinerary && <button type="button" className="btn btn-quiet schedule-apply-button" onClick={()=>applyScheduleSuggestion(item)} disabled={applyingScheduleSuggestionId!==null}>{applyingScheduleSuggestionId===item.activityId?"Applying…":"Apply this time"}</button>}
                      </div>)}
                    </div>}
                    {day.timedActivityCount<2 && <p className="schedule-analysis-note">Add times to at least two activities to analyse the break between them.</p>}
                  </article>)}
              </div>
              <p className="schedule-analysis-disclaimer">{scheduleAnalysis.disclaimer}</p>
            </section>}
            <ItineraryMap days={itinerary.days} />
            <section className="opening-hours-panel">
              <div className="section-heading">
                <div><p className="eyebrow">STAGE 34 · ATTRACTION INTELLIGENCE</p><h2>Check attraction opening hours</h2><small>Compare scheduled visits with published weekly opening times</small></div>
                <button type="button" className="btn btn-primary" onClick={analyzeOpeningHours} disabled={loadingOpeningHours}>{loadingOpeningHours?"Checking places…":"Check opening hours"}</button>
              </div>
              <p className="flight-operations-intro">Uses nearby OpenStreetMap place data and published opening_hours tags. A missing or unsupported schedule is never treated as confirmed open.</p>
              {openingHoursAnalysis && <div className="opening-hours-days">
                {openingHoursAnalysis.days.map(day=><article className="opening-hours-day" key={day.dayId}>
                  <div className="schedule-analysis-day-heading"><strong>Day {day.dayNumber} · {formatDate(day.date)}</strong><span>{day.activities.length} activities</span></div>
                  {day.activities.length===0 ? <p className="schedule-analysis-note">No activities planned for this day.</p> :
                    day.activities.map(item=><div className="opening-hours-item" key={item.activityId}>
                      <div className="opening-hours-item-heading">
                        <div><strong>{item.activityTitle}</strong><small>{item.placeName || "No place name"} · {item.startTime && item.endTime ? item.startTime+"–"+item.endTime : "Time not set"}</small></div>
                        <span className={"opening-hours-status "+item.status}>{item.status.replaceAll("_"," ")}</span>
                      </div>
                      {item.matchedPlace && <small className="opening-hours-matched">Matched place: {item.matchedPlace}</small>}
                      {item.publishedOpeningHours && <small className="opening-hours-raw">Published hours: {item.publishedOpeningHours}</small>}
                      <p>{item.recommendation}</p>
                    </div>)}
                </article>)}
              </div>}
              {openingHoursAnalysis && <p className="schedule-analysis-disclaimer">{openingHoursAnalysis.disclaimer}</p>}
            </section>
            <section className="route-analysis-panel">
              <div className="section-heading">
                <div><p className="eyebrow">STAGE 33 · ROUTE-AWARE PLANNING</p><h2>Check travel between activities</h2><small>Compare road-route estimates with the time available between stops</small></div>
                <div className="schedule-analysis-controls">
                  <label>Extra planning buffer
                    <select value={routeBuffer} onChange={e=>setRouteBuffer(e.target.value)}>
                      <option value="0">No extra buffer</option><option value="15">15 minutes</option><option value="30">30 minutes</option><option value="45">45 minutes</option><option value="60">60 minutes</option>
                    </select>
                  </label>
                  <button type="button" className="btn btn-primary" onClick={analyzeItineraryRoutes} disabled={loadingRouteAnalysis}>{loadingRouteAnalysis?"Estimating…":"Analyse road routes"}</button>
                </div>
              </div>
              <p className="flight-operations-intro">Uses saved activity coordinates and road routes. Activities without coordinates cannot be routed. No itinerary times are changed.</p>
              {routeAnalysis && <div className="route-analysis-days">
                {routeAnalysis.days.length===0 ? <p className="budget-empty">No itinerary days are available for route analysis.</p> :
                  routeAnalysis.days.map(day=><article className="route-analysis-day" key={day.dayId}>
                    <div className="schedule-analysis-day-heading"><strong>Day {day.dayNumber} · {formatDate(day.date)}</strong><span>{day.legs.length} route leg(s)</span></div>
                    {day.message && <p className="schedule-analysis-note">{day.message}</p>}
                    {day.legs.map(leg=><div className="route-analysis-leg" key={leg.fromActivityId+leg.toActivityId}>
                      <div className="route-analysis-leg-heading"><strong>{leg.fromTitle} → {leg.toTitle}</strong><span className={"schedule-transition-status "+(leg.status==="feasible"?"buffer_met":leg.status==="unavailable"?"short_gap":"overlap")}>{leg.status.replace("_"," ")}</span></div>
                      <div className="route-analysis-metrics">
                        <span>{leg.distanceKm===null?"Route unavailable":leg.distanceKm+" km"}</span>
                        <span>{leg.roadDurationMinutes===null?"—":leg.roadDurationMinutes+" min road estimate"}</span>
                        <span>{leg.gapMinutes+" min available"}</span>
                        <span>{leg.requiredMinutes===null?"":leg.requiredMinutes+" min incl. buffer"}</span>
                      </div>
                      <p>{leg.explanation}</p>
                    </div>)}
                  </article>)}
              </div>}
              {routeAnalysis && <p className="schedule-analysis-disclaimer">{routeAnalysis.disclaimer}</p>}
            </section>
            <section className="journey-timeline-panel">
              <div className="section-heading"><div><p className="eyebrow">STAGE 27 · JOURNEY OVERVIEW</p><h2>Smart travel timeline</h2><small>Flights and planned activities in chronological order</small></div><span className="timeline-count">{journeyTimeline.length} items</span></div>
              <p className="timeline-intro">Flight times use actual, then estimated, then scheduled provider times. Activity times are local itinerary times; unscheduled activities are date-only.</p>
              {sameDayActivityCount>0 && <div className="timeline-review-note">⚠ {sameDayActivityCount} planned activities share a calendar date with a flight. Review manually; this is not a confirmed timing conflict.</div>}
              {journeyTimeline.length===0 ? <p className="budget-empty">Add flights or itinerary activities to build your journey timeline.</p> :
                <div className="journey-timeline-list">{journeyTimeline.map(item=><article className="journey-timeline-item" key={item.kind+item.id}>
                  <span className={`journey-timeline-marker ${item.kind}`}>{item.kind==="flight"?"✈":"◆"}</span>
                  <div className="journey-timeline-content"><div className="journey-timeline-heading"><strong>{item.title}</strong><span>{item.status}</span></div><p>{item.timeLabel}</p><small>{item.subtitle}</small></div>
                </article>)}</div>}
            </section>
            <section className="flight-operations-panel">
              <div className="section-heading">
                <div><p className="eyebrow">STAGE 24 · JOURNEY OPERATIONS</p><h2>Flight tracking</h2><small>Keep flight details alongside your trip itinerary</small></div>
                <button type="button" className="btn btn-primary" onClick={() => setShowAddFlight(value => !value)}>{showAddFlight ? "Close form" : "＋ Add flight"}</button>
              </div>
              <p className="flight-operations-intro">Save the flight number, airports and scheduled times in your local time zone. Live status refresh requires a server-side FlightAware AeroAPI key. Until connected, flights remain marked as not checked.</p>
              {showAddFlight && <form className="flight-add-form" onSubmit={addTripFlight}>
                <label>Journey group<input value={journeyGroup} onChange={e => setJourneyGroup(e.target.value)} placeholder="Outbound / Return" maxLength={80} required /></label>
                <label>Flight number<input value={flightIdentifier} onChange={e => setFlightIdentifier(e.target.value.toUpperCase())} placeholder="UL 503" maxLength={24} required /></label>
                <label>From · IATA code<input value={flightOrigin} onChange={e => setFlightOrigin(e.target.value.toUpperCase())} placeholder="CMB" maxLength={3} minLength={3} required /></label>
                <label>To · IATA code<input value={flightDestination} onChange={e => setFlightDestination(e.target.value.toUpperCase())} placeholder="LHR" maxLength={3} minLength={3} required /></label>
                <label>Scheduled departure<input type="datetime-local" value={flightDeparture} onChange={e => setFlightDeparture(e.target.value)} required /></label>
                <label>Scheduled arrival<input type="datetime-local" value={flightArrival} onChange={e => setFlightArrival(e.target.value)} required /></label>
                <button className="btn btn-primary" disabled={busy}>{busy ? "Saving…" : "Save flight"}</button>
              </form>}
              {loadingFlights ? <p className="budget-empty">Loading saved flights…</p> : tripFlights.length === 0
                ? <p className="budget-empty">No flights saved yet. Add your flight details to start building a trip timeline.</p>
                : <div className="flight-list">{tripFlights.map(flight => <article className="flight-card" key={flight.id}>
                    <div className="flight-card-top">
                      <div><strong>{flight.flightIdentifier}</strong><small>{flight.provider === "none" ? "Traveller-entered schedule" : flight.provider}</small></div>
                      <span className={`flight-status flight-status-${flight.status}`}>{flight.status.replaceAll("_"," ")}</span>
                    </div>
                    <div className="flight-route">
                      <div><strong>{flight.originAirportCode}</strong><small>{formatFlightTime(flight.scheduledDepartureUtc, displayTimeZone)}</small></div>
                      <span>→</span>
                      <div><strong>{flight.destinationAirportCode}</strong><small>{formatFlightTime(flight.scheduledArrivalUtc, displayTimeZone)}</small></div>
                    </div>
                    {flight.departureDelayMinutes !== null && <p className="flight-provider-detail">Departure delay: {flight.departureDelayMinutes} minutes</p>}
                    {flight.providerStatus && <p className="flight-provider-detail">Provider status: {flight.providerStatus}</p>}
                    <div className="flight-card-footer">
                      <small>{flight.lastCheckedAt ? `Last checked ${formatFlightTime(flight.lastCheckedAt, displayTimeZone)}` : "Live status not checked"}</small>
                      <button type="button" className="expense-edit" disabled={refreshingFlightId === flight.id} onClick={() => refreshFlightStatus(flight)}>{refreshingFlightId === flight.id ? "Checking…" : "Refresh status"}</button>
                    </div>
                  </article>)}</div>}
              <p className="flight-operations-footnote">Same-day itinerary review is only a coarse signal; activity timing and live transfer feasibility are not modelled. No flight is booked or changed by TravelOS.</p>
            </section>
            <section className="ground-transfer-panel">
              <div className="section-heading">
                <div><p className="eyebrow">STAGE 26 · AIRPORT GROUND TRANSFERS</p><h2>Plan the airport journey</h2><small>Estimate the road leg from an airport to your destination</small></div>
              </div>
              <p className="flight-operations-intro">Enter an airport IATA code and destination. TravelOS resolves both places and requests a route estimate; check the returned airport name before relying on it.</p>
              <form className="ground-transfer-form" onSubmit={estimateGroundTransfer}>
                <label>Airport IATA code<input value={transferAirport} onChange={e => setTransferAirport(e.target.value.toUpperCase())} maxLength={3} minLength={3} placeholder="CMB" required /></label>
                <label>Destination address or place<input value={transferDestination} onChange={e => setTransferDestination(e.target.value)} minLength={4} maxLength={240} placeholder="Galle Face, Colombo" required /></label>
                <label>Travel mode<select value={transferMode} onChange={e => setTransferMode(e.target.value)}><option value="driving">Driving</option><option value="cycling">Cycling</option></select></label>
                <label>Extra pickup buffer · minutes<input type="number" min="0" max="240" value={transferBuffer} onChange={e => setTransferBuffer(e.target.value)} required /></label>
                <label>Flight arrival · optional<input type="datetime-local" value={transferArrival} onChange={e => setTransferArrival(e.target.value)} /></label>
                <button className="btn btn-primary" disabled={estimatingTransfer || !transferDestination.trim()}>{estimatingTransfer ? "Estimating route…" : "Estimate ground transfer"}</button>
              </form>
              {transferEstimate && <article className="ground-transfer-result">
                <div className="ground-transfer-result-head"><div><span className="connection-group">ROUTE ESTIMATE</span><h3>{transferEstimate.airportIataCode} · {transferEstimate.airportName.split(",")[0]}</h3></div><strong>{transferEstimate.distanceKm.toFixed(1)} km</strong></div>
                <p>To <strong>{transferEstimate.destination}</strong></p>
                <div className="ground-transfer-metrics">
                  <div><strong>{transferEstimate.roadDurationMinutes} min</strong><small>Estimated road time</small></div>
                  <div><strong>{transferEstimate.pickupBufferMinutes} min</strong><small>Added buffer</small></div>
                  <div><strong>{transferEstimate.totalPlanningMinutes} min</strong><small>Planning allowance</small></div>
                </div>
                {transferEstimate.recommendedDepartureUtc && <p>Planning departure: <strong>{new Date(transferEstimate.recommendedDepartureUtc).toLocaleString()}</strong> (based on the supplied flight arrival and buffer)</p>}
                <small className="connection-source">Route provider: {transferEstimate.provider} · Estimated {new Date(transferEstimate.estimatedAt).toLocaleString()}</small>
                <p className="ground-transfer-disclaimer">{transferEstimate.disclaimer}</p>
              </article>}
            </section>
            <section className="connection-intelligence-panel">
              <div className="section-heading">
                <div><p className="eyebrow">STAGE 25 · CONNECTION INTELLIGENCE</p><h2>Airport & connection watch</h2><small>Schedule-based assessment of consecutive flight legs</small></div>
                <button type="button" className="btn btn-secondary" onClick={refreshFlightConnections} disabled={loadingConnections || tripFlights.length < 2}>{loadingConnections ? "Assessing…" : "Reassess connections"}</button>
              </div>
              <p className="connection-intro">Flights are paired only when they share the same traveller-defined journey group. Separate Outbound and Return groups are not connected to each other.</p>
              {loadingConnections ? <p className="budget-empty">Assessing saved flight legs…</p> : flightConnections.length === 0
                ? <p className="budget-empty">No consecutive flight legs found in the same journey group. Add at least two flights to a group such as Outbound to assess a connection.</p>
                : <div className="connection-list">{flightConnections.map(connection => <article className="connection-card" key={connection.inboundFlightId + connection.outboundFlightId}>
                    <div className="connection-card-top">
                      <div><span className="connection-group">{connection.journeyGroup}</span><strong>{connection.inboundFlightIdentifier} → {connection.outboundFlightIdentifier}</strong></div>
                      <span className={`connection-risk risk-${connection.riskLevel}`}>{connection.riskLevel.replaceAll("_"," ")}</span>
                    </div>
                    <div className="connection-airports">
                      <div><strong>{connection.inboundDestinationAirportCode}</strong><small>Arrive · {new Date(connection.inboundEffectiveArrivalUtc).toLocaleString()}</small></div>
                      <div className="connection-gap"><strong>{connection.gapMinutes < 0 ? "Overlap" : `${Math.floor(connection.gapMinutes/60)}h ${connection.gapMinutes%60}m`}</strong><small>Connection gap</small></div>
                      <div><strong>{connection.outboundOriginAirportCode}</strong><small>Depart · {new Date(connection.outboundEffectiveDepartureUtc).toLocaleString()}</small></div>
                    </div>
                    <p>{connection.explanation}</p>
                    <small className="connection-source">{connection.sameAirport ? "Same airport code" : "Airport change"} · {connection.timeSource === "scheduled_only" ? "Scheduled times only" : "Includes provider estimate/actual time"}</small>
                  </article>)}</div>}
              <p className="connection-disclaimer">{connectionDisclaimer || "Heuristic estimates only. Check airline minimum connection times and airport rules."}</p>
            </section>
            <section className="weather-disruption-panel">
              <div className="section-heading">
                <div><p className="eyebrow">STAGE 23 · TRAVEL OPERATIONS</p><h2>Weather disruption watch</h2><small>Forecast evidence for your scheduled activities</small></div>
                <button type="button" className="btn btn-primary" disabled={loadingWeather || !itinerary || itinerary.days.every(day => day.activities.length === 0)} onClick={scanTripWeather}>
                  {loadingWeather ? "Scanning forecast…" : "Scan trip weather"}
                </button>
              </div>
              <p className="weather-disruption-intro">Uses Open-Meteo daily forecasts for activities with saved coordinates. Short-range forecasts are not official weather warnings and cannot guarantee conditions on the day.</p>
              {weatherScannedAt && <div className="weather-scan-meta"><span>Last scan: {new Date(weatherScannedAt).toLocaleString()}</span><span>{weatherAssessments.length} activities assessed · {weatherScanStatus || "saved forecast results"}</span></div>}
              {weatherAssessments.length > 0 && <>
                <div className="weather-summary-stats">
                  <div><strong>{weatherAssessments.length}</strong><span>ASSESSED</span></div>
                  <div><strong>{weatherAssessments.filter(item => item.severity === "high").length}</strong><span>HIGH SIGNAL</span></div>
                  <div><strong>{weatherAssessments.filter(item => item.severity === "moderate").length}</strong><span>MODERATE</span></div>
                </div>
                <div className="weather-assessment-list">
                  {weatherAssessments.map(item => <article className={`weather-assessment-card severity-${item.severity}`} key={item.id}>
                    <div className="weather-assessment-heading">
                      <div><strong>{item.activityTitle}</strong><small>{item.placeName || "Location not named"} · {formatDate(item.activityDate)}</small></div>
                      <span className={`weather-severity severity-${item.severity}`}>{item.severity}</span>
                    </div>
                    <p>{item.summary}</p>
                    <div className="weather-metrics">
                      {item.precipitationProbabilityMax !== null && <span>Rain chance <b>{item.precipitationProbabilityMax}%</b></span>}
                      {item.precipitationSumMm !== null && <span>Rain <b>{item.precipitationSumMm} mm</b></span>}
                      {item.windSpeedMaxKmh !== null && <span>Wind <b>{item.windSpeedMaxKmh} km/h</b></span>}
                      {item.temperatureMaxC !== null && <span>Max temp <b>{item.temperatureMaxC}°C</b></span>}
                    </div>
                    <p className="weather-recommendation">{item.recommendation}</p>
                    <small className="weather-source">Forecast source: <a href="https://open-meteo.com/" target="_blank" rel="noreferrer">{item.source}</a> · Retrieved {new Date(item.forecastFetchedAt).toLocaleString()}</small>
                  </article>)}
                </div>
              </>}
              {weatherSkipped.length > 0 && <div className="weather-skipped-list">
                <strong>Activities not assessed ({weatherSkipped.length})</strong>
                {weatherSkipped.map(item => <div key={item.activityId}><b>{item.activityTitle}</b><span>{formatDate(item.activityDate)} · {item.reason}</span></div>)}
              </div>}
              {!weatherAssessments.length && !weatherSkipped.length && <p className="budget-empty">{itinerary ? "No weather scan has been run yet. Add coordinates to outdoor activities, then scan the trip." : "Create an itinerary before scanning weather."}</p>}
            </section>
            <section className="group-workspace">
              <div className="section-heading"><div><p className="eyebrow">TRAVEL TOGETHER</p><h2>Trip members</h2></div><span className="count-label">{tripMembers.length} TRAVELLERS</span></div>
              <div className="member-list">{tripMembers.map(member => <div className="member-row" key={member.userId}>
                <span className="member-avatar">{initials(member.displayName)}</span>
                <div className="member-details"><strong>{member.displayName}{member.userId === user.id ? " · You" : ""}</strong><span>{member.email}</span></div>
                <span className={member.role === "owner" ? "role owner" : "role"}>{member.role}</span>
                {selectedTrip.currentUserRole === "owner" && member.role !== "owner" && <button className="delete-link" onClick={() => removeTripMember(member)}>Remove</button>}
              </div>)}</div>
              {selectedTrip.currentUserRole === "owner" && <>
                <form className="invite-form" onSubmit={sendTripInvitation}>
                  <label>Invite a registered TravelOS traveller<input type="email" value={inviteEmail} onChange={e => setInviteEmail(e.target.value)} placeholder="traveller@example.com" required /></label>
                  <button className="btn btn-primary" disabled={busy || !inviteEmail.trim()}>{busy ? "Sending…" : "＋ Invite traveller"}</button>
                </form>
                <p className="budget-footnote">Invitations are available to existing TravelOS accounts and expire after 7 days. Email delivery is not configured yet; invited travellers see requests in their TravelOS inbox.</p>
                {tripInvitations.filter(invitation => invitation.status === "pending").length > 0 && <div className="pending-invites"><strong>Pending invitations</strong>{tripInvitations.filter(invitation => invitation.status === "pending").map(invitation => <div key={invitation.id}><span>{invitation.id.slice(0,8)} · Expires {formatDate(invitation.expiresAt.slice(0,10))}</span><button className="delete-link" onClick={() => revokeTripInvitation(invitation.id)}>Revoke</button></div>)}</div>}
              </>}
            </section>


            <section className="preference-workspace">
              <div className="section-heading"><div><p className="eyebrow">MAKE THIS TRIP YOURS</p><h2>Traveller preferences</h2></div><span className="count-label">{myPreference ? "PROFILE SAVED" : "NOT SET UP"}</span></div>
              <p className="preference-intro">Tell your group what matters to you. Your interests, pace and travel style can help shape a plan everyone enjoys.</p>
              <form className="preference-form" onSubmit={savePreferences}>
                <fieldset className="interest-picker"><legend>What are you interested in? <span>Choose up to 8</span></legend>
                  <div className="interest-options">{preferenceInterestOptions.map(interest => <label className={preferenceInterests.includes(interest) ? "interest-option selected" : "interest-option"} key={interest}><input type="checkbox" checked={preferenceInterests.includes(interest)} onChange={() => togglePreferenceInterest(interest)} />{interest}</label>)}</div>
                </fieldset>
                <div className="preference-fields">
                  <label>Preferred pace<select value={preferencePace} onChange={e => setPreferencePace(e.target.value)}><option value="relaxed">Relaxed — plenty of downtime</option><option value="balanced">Balanced — mix of activities and rest</option><option value="fast">Fast — see as much as possible</option></select></label>
                  <label>Travel style<select value={preferenceStyle} onChange={e => setPreferenceStyle(e.target.value)}><option value="mixed">A bit of everything</option><option value="culture">Culture and heritage</option><option value="nature">Nature and outdoors</option><option value="adventure">Adventure and action</option><option value="food">Food and local flavours</option><option value="relaxation">Relaxation and slow travel</option></select></label>
                  <label>Daily budget (optional)<input type="number" min="0" step="0.01" value={preferenceBudget} onChange={e => setPreferenceBudget(e.target.value)} placeholder="e.g. 75" /></label>
                  <label>Budget currency<select value={preferenceCurrency} onChange={e => setPreferenceCurrency(e.target.value)}><option value="USD">USD</option><option value="LKR">LKR</option><option value="EUR">EUR</option><option value="GBP">GBP</option><option value="JPY">JPY</option><option value="AUD">AUD</option><option value="INR">INR</option><option value="SGD">SGD</option></select></label>
                </div>
                <label className="preference-notes">Anything else your group should know? <span>Optional · 500 characters</span><textarea maxLength={500} rows={3} value={preferenceNotes} onChange={e => setPreferenceNotes(e.target.value)} placeholder="e.g. I'd love a free afternoon for exploring independently." /></label>
                <div className="preference-submit"><span>Your notes are private to you. Group members can see your selected interests, pace, travel style and daily budget.</span><button className="btn btn-primary" disabled={busy}>{busy ? "Saving…" : "Save my preferences"}</button></div>
              </form>
              <div className="group-preference-list"><div className="subheading"><strong>Group preference overview</strong><span>{groupPreferences.length} of {tripMembers.length} profiles completed</span></div>
                {groupPreferences.length ? groupPreferences.map(preference => <article className="group-preference-card" key={preference.userId}>
                  <span className="member-avatar">{initials(preference.displayName)}</span>
                  <div className="group-preference-content"><strong>{preference.displayName}{preference.userId === user.id ? " · You" : ""}</strong><span>{preference.travelStyle} · {preference.pace} pace</span><div className="preference-tags">{preference.interests.map(interest => <small key={interest}>{interest}</small>)}</div></div>
                  {preference.dailyBudgetAmount != null && <b className="preference-budget">{preference.dailyBudgetAmount.toLocaleString()} {preference.dailyBudgetCurrencyCode}/day</b>}
                </article>) : <p className="budget-empty">No one has shared preferences yet. Be the first to add yours.</p>}
              </div>
            </section>


            <section className="poll-workspace">
              <div className="section-heading"><div><p className="eyebrow">DECIDE TOGETHER</p><h2>Group decisions</h2></div><span className="count-label">{groupPolls.filter(poll => poll.status === "open").length} OPEN POLLS</span></div>
              <p className="poll-intro">Can't agree on the next activity or where to eat? Create a poll and let every trip member vote.</p>
              <form className="poll-create-form" onSubmit={createGroupPoll}>
                <label>What should the group decide?<input value={pollTitle} onChange={e => setPollTitle(e.target.value)} placeholder="e.g. Which day should we visit the national park?" minLength={3} maxLength={160} required /></label>
                <label>Extra context <span>Optional</span><textarea value={pollDescription} onChange={e => setPollDescription(e.target.value)} rows={2} maxLength={1000} placeholder="Add details to help everyone decide…" /></label>
                <div className="poll-option-editor"><strong>Voting options <span>2–8 choices</span></strong>
                  {pollOptions.map((option,index) => <div className="poll-option-input" key={index}><input value={option} onChange={e => updatePollOption(index,e.target.value)} placeholder={`Option ${index+1}`} maxLength={160} required={index<2} /><button type="button" className="delete-link" disabled={pollOptions.length<=2} onClick={() => setPollOptions(current => current.filter((_,i) => i!==index))}>Remove</button></div>)}
                  {pollOptions.length<8 && <button type="button" className="add-option-link" onClick={() => setPollOptions(current => [...current,""])}>＋ Add another option</button>}
                </div>
                <div className="poll-create-footer"><label>Optional closing time<input type="datetime-local" value={pollClosesAt} onChange={e => setPollClosesAt(e.target.value)} /></label><button className="btn btn-primary" disabled={busy}>{busy ? "Creating…" : "Create group poll"}</button></div>
              </form>
              <div className="poll-list">
                {groupPolls.length ? groupPolls.map(poll => <article className="poll-card" key={poll.id}>
                  <div className="poll-card-heading"><div><span className={poll.status === "open" ? "poll-status open" : "poll-status"}>{poll.status}</span><h3>{poll.title}</h3>{poll.description && <p>{poll.description}</p>}<small>Created by {poll.createdByName} · {poll.totalVotes} {poll.totalVotes === 1 ? "vote" : "votes"}{poll.closesAt ? ` · Closes ${new Date(poll.closesAt).toLocaleString()}` : ""}</small></div>
                    {(poll.createdByUserId === user.id || selectedTrip.currentUserRole === "owner") && poll.status === "open" && <button className="delete-link" onClick={() => closeGroupPoll(poll)}>Close poll</button>}
                  </div>
                  <div className="poll-options">{poll.options.map(option => {
                    const percent = poll.totalVotes ? Math.round(option.voteCount / poll.totalVotes * 100) : 0;
                    return <button type="button" key={option.id} className={option.isSelected ? "poll-option selected" : "poll-option"} disabled={poll.status !== "open"} onClick={() => castGroupVote(poll,option.id)}>
                      <span className="poll-option-top"><b>{option.label}</b><span>{option.voteCount} · {percent}% {option.isSelected ? "✓ Your vote" : ""}</span></span><span className="poll-bar"><i style={{width:`${percent}%`}} /></span>
                    </button>;
                  })}</div>
                  <small className="poll-help">{poll.status === "open" ? "Select an option to vote. You can change your vote until the poll closes." : "Voting is closed. These are the final results."}</small>
                </article>) : <p className="budget-empty">No group polls yet. Create one to make your first decision together.</p>}
              </div>
            </section>


            <section className="expense-workspace">
              <div className="section-heading"><div><p className="eyebrow">SHARED TRIP COSTS</p><h2>Expense tracker</h2></div><span className="count-label">{tripExpenses.length} EXPENSES</span></div>
              <p className="expense-intro">Record a payment, choose who shared it and split the cost equally, by percentage or by exact amounts.</p>
              <form className="expense-create-form" onSubmit={createTripExpense}>
                <div className="expense-fields">
                  <label>What was paid for?<input value={expenseTitle} onChange={e => setExpenseTitle(e.target.value)} placeholder="e.g. Dinner at the beach" maxLength={160} required /></label>
                  <label>Category<select value={expenseCategory} onChange={e => setExpenseCategory(e.target.value)}><option value="accommodation">Accommodation</option><option value="transport">Transport</option><option value="food">Food</option><option value="activities">Activities</option><option value="shopping">Shopping</option><option value="other">Other</option></select></label>
                  <label>Amount<input type="number" min="0.0001" step="0.0001" value={expenseAmount} onChange={e => { setExpenseAmount(e.target.value); if (expenseSplitType === "exact") setExpenseCustomShares(defaultCustomShareValues("exact", e.target.value, expenseParticipants)); }} placeholder="0.00" required /></label>
                  <label>Currency<select value={expenseCurrency} onChange={e => setExpenseCurrency(e.target.value)}><option value="USD">USD</option><option value="LKR">LKR</option><option value="EUR">EUR</option><option value="GBP">GBP</option><option value="JPY">JPY</option><option value="AUD">AUD</option><option value="INR">INR</option><option value="SGD">SGD</option></select></label>
                  <label>Date<input type="date" value={expenseDate} onChange={e => setExpenseDate(e.target.value)} required /></label>
                  <label>Who paid?<select value={expensePaidBy} onChange={e => setExpensePaidBy(e.target.value)} required>{tripMembers.map(member => <option value={member.userId} key={member.userId}>{member.displayName}</option>)}</select></label>
                </div>
                <label className="expense-split-type">How should this expense be split?<select value={expenseSplitType} onChange={e => changeExpenseSplitType(e.target.value as "equal" | "percentage" | "exact")}><option value="equal">Equal shares</option><option value="percentage">By percentage</option><option value="exact">Exact amounts</option></select></label>
                <fieldset className="expense-participants"><legend>Split between <span>Choose the travellers sharing this cost</span></legend>
                  <div>{tripMembers.map(member => <label key={member.userId}><input type="checkbox" checked={expenseParticipants.includes(member.userId)} onChange={() => toggleExpenseParticipant(member.userId)} />{member.displayName}{member.userId === expensePaidBy ? " · Payer" : ""}</label>)}</div>
                </fieldset>
                {expenseSplitType !== "equal" && <div className="custom-share-editor">
                  <div className="custom-share-heading"><strong>{expenseSplitType === "percentage" ? "Share percentages" : "Individual amounts"}</strong><span>{expenseSplitType === "percentage" ? "Must total exactly 100%" : `Must total ${expenseAmount || "0"} ${expenseCurrency}`}</span></div>
                  {expenseParticipants.map(id => {
                    const member = tripMembers.find(x => x.userId === id);
                    const defaultValue = defaultCustomShareValues(expenseSplitType, expenseAmount, expenseParticipants)[id] ?? "";
                    return <label key={id}><span>{member?.displayName ?? "Traveller"}</span><div><input type="number" min="0.0001" step="0.0001" value={expenseCustomShares[id] ?? defaultValue} onChange={e => setExpenseCustomShares(current => ({...current,[id]:e.target.value}))} required /><small>{expenseSplitType === "percentage" ? "%" : expenseCurrency}</small></div></label>;
                  })}
                  <button type="button" className="custom-share-reset" onClick={() => setExpenseCustomShares(defaultCustomShareValues(expenseSplitType, expenseAmount, expenseParticipants))}>Distribute {expenseSplitType === "percentage" ? "percentages" : "amount"} evenly</button>
                </div>}
                <label className="expense-notes">Notes <span>Optional</span><textarea rows={2} maxLength={500} value={expenseNotes} onChange={e => setExpenseNotes(e.target.value)} placeholder="Add a receipt reference or a short note…" /></label>
                <div className="expense-submit"><small>{expenseParticipants.length} travellers selected · {expenseSplitType === "equal" ? "Equal split" : expenseSplitType === "percentage" ? "Percentage split" : "Exact amount split"} · Currency kept as entered</small><button className="btn btn-primary" disabled={busy || !expenseTitle.trim() || !expenseAmount || !expensePaidBy || !expenseParticipants.includes(expensePaidBy) || expenseParticipants.length===0 || (expenseSplitType !== "equal" && expenseParticipants.some(id => !(Number(expenseCustomShares[id] ?? defaultCustomShareValues(expenseSplitType, expenseAmount, expenseParticipants)[id]) > 0)))}>{busy ? "Saving…" : "＋ Add shared expense"}</button></div>
              </form>
              <section className="currency-intelligence-panel">
                <div className="section-heading"><div><p className="eyebrow">GLOBAL CURRENCY</p><h3>Historical currency conversion</h3></div><span className="count-label">{currencyOptions.length} SUPPORTED ISO CURRENCIES</span></div>
                <div className="currency-convert-toolbar">
                  <label>Convert expenses into
                    <select value={conversionTargetCurrency} onChange={event => setConversionTargetCurrency(event.target.value)}>
                      {currencyOptions.map(item => <option key={item.isoCode} value={item.isoCode}>{item.isoCode} — {item.name}</option>)}
                    </select>
                  </label>
                  <p>Choose a target currency, then use “Convert expense” on any recorded expense. TravelOS looks up the reference rate for that expense's date.</p>
                </div>
                {currencyConversions.length > 0 ? <div className="currency-snapshot-list">
                  <div className="subheading"><strong>Saved conversion snapshots</strong><span>Newest first · original expenses unchanged</span></div>
                  {currencyConversions.slice(0,8).map(item => <article key={item.id}>
                    <div><strong>{item.originalAmount.toLocaleString()} {item.originalCurrencyCode}</strong><span>→</span><strong>{item.convertedAmount.toLocaleString()} {item.convertedCurrencyCode}</strong></div>
                    <small>Rate: 1 {item.originalCurrencyCode} = {item.exchangeRate.toLocaleString(undefined,{maximumFractionDigits:8})} {item.convertedCurrencyCode} · Effective {formatDate(item.rateDate)}</small>
                    <small>{item.provider} · Saved {new Date(item.createdAt).toLocaleString()}</small>
                  </article>)}
                </div> : <p className="budget-empty">No conversion snapshots yet. Convert an expense to preserve its historical reference rate.</p>}
                <small className="currency-disclaimer">Reference rates are estimates, not bank, card-network or cash-exchange quotes. The provider's effective publication date may differ from the expense date on weekends and holidays.</small>
              </section>
              <div className="expense-list">
                {tripExpenses.length ? tripExpenses.map(expense => <article className="expense-card" key={expense.id}>
                  <div className="expense-card-top"><div><span className="expense-category">{expense.category}</span><h3>{expense.title}</h3><small>{formatDate(expense.expenseDate)} · Paid by {expense.paidByName}</small></div><div className="expense-total"><strong>{expense.amount.toLocaleString()} {expense.currencyCode}</strong><small>{expense.shares.length} shares</small></div></div>
                  <div className="expense-shares">{expense.shares.map(share => <div key={share.userId}><span>{share.displayName}{share.userId === expense.paidByUserId ? " (paid)" : ""}</span><b>{share.shareAmount.toLocaleString()} {expense.currencyCode}</b></div>)}</div>
                  <div className="expense-conversion-actions">
                    <button type="button" className="expense-edit" disabled={convertingExpenseId === expense.id || expense.currencyCode === conversionTargetCurrency} onClick={() => convertExpenseCurrency(expense)}>
                      {convertingExpenseId === expense.id ? "Converting…" : `Convert expense to ${conversionTargetCurrency}`}
                    </button>
                    {currencyConversions.find(item => item.expenseId === expense.id && item.convertedCurrencyCode === conversionTargetCurrency) && <small>Latest saved estimate: {currencyConversions.find(item => item.expenseId === expense.id && item.convertedCurrencyCode === conversionTargetCurrency)!.convertedAmount.toLocaleString()} {conversionTargetCurrency}</small>}
                  </div>
                  {(expense.createdByUserId === user.id || selectedTrip.currentUserRole === "owner") && <div className="expense-actions"><button type="button" className="expense-edit" onClick={() => editingExpenseId === expense.id ? setEditingExpenseId(null) : beginEditExpense(expense)}>Edit expense</button><button className="delete-link expense-delete" onClick={() => deleteTripExpense(expense)}>Remove expense</button></div>}
                  <button type="button" className="expense-history-toggle" onClick={() => loadExpenseHistory(expense)}>{expandedExpenseHistory === expense.id ? "Hide change history" : "View change history"}</button>
                  {expandedExpenseHistory === expense.id && <div className="expense-history-list">{(expenseAuditHistory[expense.id] ?? []).length ? expenseAuditHistory[expense.id].map(entry => <article key={entry.id}><strong>{entry.action === "created" ? "Created" : entry.action === "updated" ? "Updated" : "Removed"} by {entry.changedByName}</strong><small>{new Date(entry.changedAt).toLocaleString()}</small>{entry.action === "updated" && <details><summary>View before and after</summary><pre>{JSON.stringify(JSON.parse(entry.beforeJson),null,2)}</pre><pre>{JSON.stringify(JSON.parse(entry.afterJson),null,2)}</pre></details>}</article>) : <small>No changes recorded.</small>}</div>}
                  {editingExpenseId === expense.id && <form className="expense-edit-form" onSubmit={event => saveExpenseEdit(event,expense)}>
                    <strong>Edit expense</strong>
                    <label>Title<input value={editExpenseDraft.title} maxLength={160} onChange={e => setEditExpenseDraft(d => ({...d,title:e.target.value}))} required /></label>
                    <div className="expense-edit-grid">
                      <label>Category<select value={editExpenseDraft.category} onChange={e => setEditExpenseDraft(d => ({...d,category:e.target.value}))}><option value="accommodation">Accommodation</option><option value="transport">Transport</option><option value="food">Food</option><option value="activities">Activities</option><option value="shopping">Shopping</option><option value="other">Other</option></select></label>
                      <label>Amount<input type="number" min="0.0001" step="0.0001" value={editExpenseDraft.amount} onChange={e => setEditExpenseDraft(d => ({...d,amount:e.target.value}))} required /></label>
                      <label>Currency<input maxLength={3} value={editExpenseDraft.currencyCode} onChange={e => setEditExpenseDraft(d => ({...d,currencyCode:e.target.value.toUpperCase()}))} required /></label>
                      <label>Date<input type="date" value={editExpenseDraft.expenseDate} onChange={e => setEditExpenseDraft(d => ({...d,expenseDate:e.target.value}))} required /></label>
                      <label>Who paid?<select value={editExpenseDraft.paidByUserId} onChange={e => setEditExpenseDraft(d => ({...d,paidByUserId:e.target.value}))}>{tripMembers.map(m => <option key={m.userId} value={m.userId}>{m.displayName}</option>)}</select></label>
                    </div>
                    <label>Notes<textarea value={editExpenseDraft.notes} maxLength={500} onChange={e => setEditExpenseDraft(d => ({...d,notes:e.target.value}))} /></label>
                    <div className="edit-share-list"><strong>Exact shares ({editExpenseDraft.currencyCode})</strong>{expense.shares.map(share => <label key={share.userId}><span>{share.displayName}</span><input type="number" min="0.0001" step="0.0001" value={editExpenseShares[share.userId] ?? ""} onChange={e => setEditExpenseShares(s => ({...s,[share.userId]:e.target.value}))} required /></label>)}</div>
                    <small>Shares must remain positive and add up exactly to the edited total. Changing the currency is blocked if a confirmed repayment exists in either currency.</small>
                    <div className="expense-actions"><button type="submit" className="btn btn-primary" disabled={busy || Object.values(editExpenseShares).some(v => !(Number(v)>0)) || Math.abs(Object.values(editExpenseShares).reduce((sum,v)=>sum+Number(v),0)-Number(editExpenseDraft.amount))>0.00005}>Save changes</button><button type="button" className="expense-edit" onClick={() => setEditingExpenseId(null)}>Cancel</button></div>
                  </form>}
                </article>) : <p className="budget-empty">No shared expenses yet. Add the first group payment above.</p>}
              </div>

              <div className="receipt-workspace">
                <div className="subheading"><strong>Receipt library</strong><span>{tripReceipts.length} receipts</span></div>
                <p className="expense-intro">Attach receipt images to this trip. AI extraction is a suggestion only; check every field before confirming.</p>
                <form className="receipt-upload-form" onSubmit={uploadTripReceipt}>
                  <label>Receipt image<input type="file" accept="image/jpeg,image/png,image/webp" onChange={e => setReceiptFile(e.target.files?.[0] ?? null)} required /></label>
                  <label>Attach to expense (optional)<select value={receiptExpenseId} onChange={e => setReceiptExpenseId(e.target.value)}><option value="">Keep unlinked for now</option>{tripExpenses.map(expense => <option key={expense.id} value={expense.id}>{expense.title} · {expense.amount} {expense.currencyCode}</option>)}</select></label>
                  <button className="btn btn-primary" type="submit" disabled={busy || !receiptFile}>Upload receipt</button>
                  <small>JPEG, PNG or WebP · Maximum 8 MB. Images are private to trip members.</small>
                </form>
                <div className="receipt-list">
                  {tripReceipts.length ? tripReceipts.map(receipt => <article className="receipt-card" key={receipt.id}>
                    <div className="receipt-card-top"><div><strong>{receipt.originalFileName}</strong><small>{(receipt.fileSizeBytes/1024).toFixed(0)} KB · {formatDate(receipt.uploadedAt.slice(0,10))}</small></div><span className={receipt.extractionStatus === "confirmed" ? "receipt-status confirmed" : "receipt-status"}>{receipt.extractionStatus === "confirmed" ? "Confirmed" : receipt.extractionStatus === "needs_review" ? "Review extracted details" : "Manual review"}</span></div>
                    {receipt.isDuplicate && <p className="receipt-warning">Possible duplicate: an identical image is already attached to this trip.</p>}
                    {receipt.extractionWarning && <p className="receipt-warning">{receipt.extractionWarning}</p>}
                    {receipt.extractionStatus === "confirmed" ? <div className="receipt-confirmed-details"><strong>{receipt.merchantName || "Merchant not recorded"}</strong><span>{receipt.totalAmount === null ? "Amount not recorded" : `${receipt.totalAmount.toLocaleString()} ${receipt.currencyCode ?? ""}`}</span><small>{receipt.receiptDate ? formatDate(receipt.receiptDate) : "Date not recorded"}</small></div> : <div className="receipt-review-form">
                      <label>Merchant<input value={receiptDrafts[receipt.id]?.merchantName ?? ""} onChange={e => setReceiptDrafts(d => ({...d,[receipt.id]:{...d[receipt.id],merchantName:e.target.value}}))} maxLength={180} placeholder="Extracted merchant" /></label>
                      <div className="receipt-fields-grid">
                        <label>Receipt date<input type="date" value={receiptDrafts[receipt.id]?.receiptDate ?? ""} onChange={e => setReceiptDrafts(d => ({...d,[receipt.id]:{...d[receipt.id],receiptDate:e.target.value}}))} /></label>
                        <label>Total amount<input type="number" min="0" step="0.0001" value={receiptDrafts[receipt.id]?.totalAmount ?? ""} onChange={e => setReceiptDrafts(d => ({...d,[receipt.id]:{...d[receipt.id],totalAmount:e.target.value}}))} /></label>
                        <label>Currency<input maxLength={3} value={receiptDrafts[receipt.id]?.currencyCode ?? ""} onChange={e => setReceiptDrafts(d => ({...d,[receipt.id]:{...d[receipt.id],currencyCode:e.target.value.toUpperCase()}}))} placeholder="USD" /></label>
                        <label>Link expense<select value={receiptDrafts[receipt.id]?.expenseId ?? ""} onChange={e => setReceiptDrafts(d => ({...d,[receipt.id]:{...d[receipt.id],expenseId:e.target.value}}))}><option value="">No linked expense</option>{tripExpenses.map(expense => <option key={expense.id} value={expense.id}>{expense.title}</option>)}</select></label>
                      </div>
                      {receipt.confidence !== null && <small>Extraction confidence: {(receipt.confidence*100).toFixed(0)}% · Verify before confirming.</small>}
                      <button type="button" className="btn btn-primary" disabled={busy} onClick={() => confirmTripReceipt(receipt)}>Confirm receipt details</button>
                    </div>}
                    <div className="receipt-card-actions"><a href={`${API_URL}/api/v1/trips/${selectedTrip.id}/receipts/${receipt.id}/file`} onClick={async event => {event.preventDefault(); if(!token)return; try {const response=await fetch(event.currentTarget.href,{headers:{Authorization:`Bearer ${token}`}});if(!response.ok)throw new Error("Unable to open receipt.");const blob=await response.blob();const url=URL.createObjectURL(blob);const link=document.createElement("a");link.href=url;link.download=receipt.originalFileName;link.click();URL.revokeObjectURL(url);}catch(err){setError(err instanceof Error?err.message:"Unable to open receipt.");}}}>View original file</a><button type="button" className="delete-link" onClick={() => deleteTripReceipt(receipt)}>Delete</button></div>
                  </article>) : <p className="budget-empty">No receipts uploaded yet.</p>}
                </div>
              </div>

              <div className="settlement-panel">
                <div className="subheading"><strong>Suggested repayments</strong><span>Based on recorded expenses</span></div>
                {tripSettlement?.currencies.length ? tripSettlement.currencies.map(currency => <div className="settlement-currency" key={currency.currencyCode}>
                  <div className="settlement-currency-heading"><strong>{currency.currencyCode}</strong><span>{currency.totalExpenses.toLocaleString()} {currency.currencyCode} recorded</span></div>
                  <div className="settlement-balances">{currency.balances.map(balance => <div key={balance.userId}><span>{balance.displayName}</span><b className={balance.netAmount > 0 ? "balance-positive" : "balance-negative"}>{balance.netAmount > 0 ? "+" : ""}{balance.netAmount.toLocaleString()} {currency.currencyCode}</b></div>)}</div>
                  {currency.suggestedTransfers.length ? <div className="settlement-transfers"><strong>Suggested transfers</strong>{currency.suggestedTransfers.map((transfer,index) => <div key={index}><span>{transfer.fromName} <i>pays</i> {transfer.toName}</span><b>{transfer.amount.toLocaleString()} {currency.currencyCode}</b>{user?.id === transfer.fromUserId && <button type="button" className="repayment-action" onClick={() => beginRepayment(transfer,currency.currencyCode)}>Record repayment</button>}</div>)}</div> : <p className="budget-empty">Everyone is even in this currency.</p>}
                </div>) : <p className="budget-empty">Add shared expenses to calculate who owes whom.</p>}

                {repaymentTarget && <form className="repayment-form" onSubmit={submitRepayment}>
                  <div><strong>Record payment to {repaymentTarget.toName}</strong><button type="button" className="repayment-cancel" onClick={() => setRepaymentTarget(null)}>Cancel</button></div>
                  <label>Amount ({repaymentTarget.currencyCode})<input type="number" min="0.0001" max={repaymentTarget.suggestedAmount} step="0.0001" value={repaymentAmount} onChange={e => setRepaymentAmount(e.target.value)} required /></label>
                  <label>Note (optional)<input value={repaymentNote} maxLength={500} onChange={e => setRepaymentNote(e.target.value)} placeholder="e.g. Bank transfer reference" /></label>
                  <p>Partial repayments are allowed. The recipient must confirm receipt.</p>
                  <button className="btn btn-primary" disabled={busy || Number(repaymentAmount)<=0 || Number(repaymentAmount)>repaymentTarget.suggestedAmount}>Submit repayment</button>
                </form>}
                <div className="repayment-history">
                  <div className="subheading"><strong>Repayment history</strong><span>{tripRepayments.length} records</span></div>
                  {tripRepayments.length ? tripRepayments.map(repayment => <article className="repayment-record" key={repayment.id}>
                    <div className="repayment-record-main"><strong>{repayment.fromName} <i>→</i> {repayment.toName}</strong><b>{repayment.amount.toLocaleString()} {repayment.currencyCode}</b></div>
                    <div className="repayment-record-meta"><span>{formatDate(repayment.createdAt.slice(0,10))}{repayment.note ? ` · ${repayment.note}` : ""}</span><span className={`repayment-status status-${repayment.status}`}>{repayment.status}</span></div>
                    {repayment.status === "pending" && user?.id === repayment.toUserId && <div className="repayment-response"><span>Did you receive this payment?</span><div><button type="button" disabled={busy} onClick={() => respondToRepayment(repayment,"confirm")}>Confirm received</button><button type="button" disabled={busy} onClick={() => respondToRepayment(repayment,"reject")}>Reject</button></div></div>}
                    {repayment.status === "pending" && user?.id === repayment.fromUserId && <p className="repayment-waiting">Waiting for recipient confirmation</p>}
                  </article>) : <p className="budget-empty">No repayments have been recorded yet.</p>}
                </div>

                <p className="expense-footnote">Only recipient-confirmed repayments reduce outstanding balances. Pending and rejected records remain visible in the history.</p>
              </div>

            </section>

            <section className="budget-workspace">
              <div className="section-heading"><div><p className="eyebrow">TRIP FINANCES</p><h2>Budget overview</h2></div>{budget && <span className="count-label">{budget.items.length} PLANNED ITEMS</span>}</div>
              <section className="budget-alerts-panel">
                <div className="section-heading">
                  <div><p className="eyebrow">PROACTIVE TRIP FINANCE</p><h3>Budget alerts</h3><small>Shared with everyone in this trip</small></div>
                  <button type="button" className="btn btn-primary" disabled={evaluatingBudgetAlerts || !budget} onClick={evaluateBudgetAlerts}>
                    {evaluatingBudgetAlerts ? "Checking…" : "Check budget"}
                  </button>
                </div>
                <p className="budget-alerts-help">Checks 50%, 75%, 90% and 100% of your budget, plus a possible overspend forecast. Alerts are not duplicated for the same budget amount and threshold.</p>
                {loadingBudgetAlerts ? <p className="budget-empty">Loading alert history…</p>
                  : budgetAlerts.length ? <div className="budget-alert-list">
                    {budgetAlerts.map(alert => <article className={`budget-alert-row ${alert.isRead ? "is-read" : "is-unread"}`} key={alert.id}>
                      <div className="budget-alert-marker">{alert.alertType === "projected_over_budget" ? "↗" : alert.thresholdPercent + "%"}</div>
                      <div className="budget-alert-content">
                        <div className="budget-alert-title"><strong>{alert.alertType === "projected_over_budget" ? "Projected overspending" : `${alert.thresholdPercent}% budget threshold`}</strong><small>{new Date(alert.triggeredAt).toLocaleString()}</small></div>
                        <p>{alert.message}</p>
                        <small>Actual: {alert.actualAmountSnapshot.toLocaleString()} {alert.currencyCode}{alert.projectedAmountSnapshot !== null ? ` · Projected: ${alert.projectedAmountSnapshot.toLocaleString()} ${alert.currencyCode}` : ""}</small>
                      </div>
                      {!alert.isRead && <button type="button" className="expense-edit" onClick={() => markBudgetAlertRead(alert.id)}>Mark read</button>}
                      {alert.isRead && <span className="budget-alert-read">Read</span>}
                    </article>)}
                  </div> : <p className="budget-empty">No budget alerts yet. Select “Check budget” after adding expenses or changing your budget.</p>}
              </section>
              <div className="expense-intelligence-panel">
                <div className="subheading"><strong>Smart spending insights</strong><span>{loadingExpenseIntelligence ? "Refreshing…" : "Based on recorded trip data"}</span></div>
                {expenseIntelligence ? <>
                  <div className="intelligence-stat-grid">
                    <div><span>RECORDED EXPENSES</span><strong>{expenseIntelligence.expenseCount}</strong></div>
                    <div><span>RECEIPTS</span><strong>{expenseIntelligence.receiptCount}</strong></div>
                    <div><span>RECEIPT FLAGS</span><strong>{expenseIntelligence.receiptMismatches.length}</strong></div>
                    <div><span>POSSIBLE DUPLICATES</span><strong>{expenseIntelligence.duplicateExpenseCandidates.length}</strong></div>
                  </div>
                  <div className="intelligence-subsection">
                    <div className="subheading"><strong>Actual spending by currency</strong><span>No cross-currency conversion</span></div>
                    {expenseIntelligence.actualsByCurrency.length ? <div className="intelligence-currency-list">{expenseIntelligence.actualsByCurrency.map(actual => <article key={actual.currencyCode}>
                      <div className="intelligence-currency-heading"><strong>{actual.currencyCode}</strong><b>{actual.totalAmount.toLocaleString()} {actual.currencyCode}</b></div>
                      <small>{actual.expenseCount} recorded expenses</small>
                      {actual.categories.map(category => <div className="intelligence-category-row" key={category.category}><span>{category.category}</span><b>{category.totalAmount.toLocaleString()} {actual.currencyCode}</b></div>)}
                    </article>)}</div> : <p className="budget-empty">Add expenses to see spending insights.</p>}
                  </div>
                  <div className="intelligence-subsection">
                    <div className="subheading"><strong>Budget forecast</strong><span>{expenseIntelligence.forecast.currencyCode ?? "Not configured"}</span></div>
                    {expenseIntelligence.forecast.budgetAmount !== null ? <div className="forecast-card">
                      <div className="forecast-status-row"><strong>{expenseIntelligence.forecast.status === "over_budget" ? "Budget exceeded" : expenseIntelligence.forecast.status === "projected_over_budget" ? "Possible overspend" : "Current projection"}</strong><span>{expenseIntelligence.forecast.currencyCode}</span></div>
                      <div className="forecast-metrics">
                        <div><small>Budget</small><b>{expenseIntelligence.forecast.budgetAmount.toLocaleString()}</b></div>
                        <div><small>Actual</small><b>{(expenseIntelligence.forecast.actualAmount ?? 0).toLocaleString()}</b></div>
                        <div><small>Remaining</small><b>{(expenseIntelligence.forecast.remainingAmount ?? 0).toLocaleString()}</b></div>
                        <div><small>Projected total</small><b>{expenseIntelligence.forecast.projectedTotal === null ? "—" : expenseIntelligence.forecast.projectedTotal.toLocaleString()}</b></div>
                      </div>
                      <div className="forecast-track"><span style={{width:`${Math.min(100,Math.max(0,((expenseIntelligence.forecast.actualAmount ?? 0)/Math.max(1,expenseIntelligence.forecast.budgetAmount))*100))}%`}} /></div>
                      <p>{expenseIntelligence.forecast.explanation}</p>
                      <small>Average daily spend: {expenseIntelligence.forecast.averageDailySpend === null ? "Not enough trip activity yet" : `${expenseIntelligence.forecast.averageDailySpend.toLocaleString()} ${expenseIntelligence.forecast.currencyCode}/day`} · {expenseIntelligence.forecast.elapsedDays} elapsed days · {expenseIntelligence.forecast.remainingDays} remaining days</small>
                    </div> : <p className="budget-empty">Set a trip budget cap to enable forecasting.</p>}
                  </div>
                  <div className="intelligence-subsection">
                    <div className="subheading"><strong>Planned vs actual by category</strong><span>Matched by currency and category</span></div>
                    {expenseIntelligence.categoryComparisons.length ? <div className="category-comparison-list">{expenseIntelligence.categoryComparisons.map((item,index) => <div className="category-comparison" key={item.currencyCode+item.category+index}>
                      <div><strong>{item.category}</strong><small>{item.currencyCode}</small></div>
                      <span>Planned <b>{item.plannedAmount.toLocaleString()}</b></span><span>Actual <b>{item.actualAmount.toLocaleString()}</b></span>
                      {item.actualAmount > item.plannedAmount && <em>Over plan</em>}
                    </div>)}</div> : <p className="budget-empty">Add planned category amounts and expenses to compare them.</p>}
                  </div>
                  {expenseIntelligence.receiptMismatches.length > 0 && <div className="intelligence-subsection">
                    <div className="subheading"><strong>Receipt and expense differences</strong><span>{expenseIntelligence.receiptMismatches.length} flags</span></div>
                    {expenseIntelligence.receiptMismatches.map(item => <article className="intelligence-alert" key={item.receiptId}><strong>{item.fileName}</strong><p>Receipt: {item.receiptAmount.toLocaleString()} {item.receiptCurrency ?? ""} · Expense: {item.expenseAmount.toLocaleString()} {item.expenseCurrency}</p><small>{item.explanation}</small></article>)}
                  </div>}
                  {expenseIntelligence.duplicateExpenseCandidates.length > 0 && <div className="intelligence-subsection">
                    <div className="subheading"><strong>Possible duplicate expenses</strong><span>{expenseIntelligence.duplicateExpenseCandidates.length} groups</span></div>
                    {expenseIntelligence.duplicateExpenseCandidates.map((group,index) => <article className="intelligence-alert" key={group.expenseDate+group.currencyCode+index}><strong>{group.amount.toLocaleString()} {group.currencyCode} · {formatDate(group.expenseDate)}</strong>{group.expenses.map(item => <small key={item.expenseId}>{item.title} · {item.category}</small>)}<p>{group.explanation}</p></article>)}
                  </div>}
                  <div className="intelligence-footer"><small>Insights are deterministic checks, not financial advice. Review flagged entries yourself; TravelOS never edits or deletes expenses automatically.</small><button type="button" className="expense-edit" disabled={loadingExpenseIntelligence} onClick={() => loadExpenseIntelligence(selectedTrip)}>Refresh insights</button></div>
                </> : <p className="budget-empty">{loadingExpenseIntelligence ? "Calculating trip spending…" : "No insights are available yet."}</p>}
              </div>
              <section className="multi-currency-budget-panel">
                <div className="section-heading"><div><p className="eyebrow">STAGE 21 · GLOBAL FINANCE</p><h3>Multi-currency budget intelligence</h3></div><label className="reporting-currency-field">Reporting currency
                  <select value={budgetReportingCurrency} onChange={event => {
                    const next = event.target.value;
                    setBudgetReportingCurrency(next);
                    if (selectedTrip) loadBudgetIntelligence(selectedTrip, next);
                  }}>
                    {(currencyOptions.length ? currencyOptions : [{isoCode:selectedTrip.currencyCode,name:selectedTrip.currencyCode,symbol:null}]).map(item => <option key={item.isoCode} value={item.isoCode}>{item.isoCode} — {item.name}</option>)}
                  </select>
                </label></div>
                {budgetIntelligence ? <>
                  <div className="multi-currency-summary">
                    <div><span>CONVERTED ACTUAL SPEND</span><strong>{budgetIntelligence.convertedActualTotal.toLocaleString()} {budgetIntelligence.reportingCurrencyCode}</strong><small>{budgetIntelligence.convertedExpenseCount} of {budgetIntelligence.expenseCount} expenses included</small></div>
                    <div><span>BUDGET CAP</span><strong>{budgetIntelligence.budgetAmount === null ? "Not comparable" : budgetIntelligence.budgetAmount.toLocaleString() + " " + budgetIntelligence.reportingCurrencyCode}</strong><small>{budgetIntelligence.budgetCurrencyCode ? "Budget currency: " + budgetIntelligence.budgetCurrencyCode : "No budget configured"}</small></div>
                    <div><span>REMAINING BUDGET</span><strong>{budgetIntelligence.remainingBudget === null ? "—" : budgetIntelligence.remainingBudget.toLocaleString() + " " + budgetIntelligence.reportingCurrencyCode}</strong><small>{budgetIntelligence.remainingBudget === null ? "Requires matching budget currency" : "Based on converted expenses"}</small></div>
                    <div><span>PROJECTED TOTAL</span><strong>{budgetIntelligence.projectedTotal === null ? "—" : budgetIntelligence.projectedTotal.toLocaleString() + " " + budgetIntelligence.reportingCurrencyCode}</strong><small>{budgetIntelligence.averageDailySpend === null ? "Not enough trip activity" : budgetIntelligence.averageDailySpend.toLocaleString() + " per day"}</small></div>
                  </div>
                  <div className="multi-currency-status"><strong>{budgetIntelligence.status.replaceAll("_"," ")}</strong><p>{budgetIntelligence.explanation}</p></div>
                  {budgetIntelligence.categoryTotals.length > 0 && <div className="intelligence-subsection">
                    <div className="subheading"><strong>Spending by category</strong><span>{budgetIntelligence.reportingCurrencyCode}</span></div>
                    {budgetIntelligence.categoryTotals.map(item => <div className="category-comparison" key={item.category}>
                      <div><strong>{item.category}</strong><small>{item.expenseCount} expenses</small></div>
                      <span>Actual <b>{item.actualAmount.toLocaleString()}</b></span><span>Planned <b>{item.plannedAmount.toLocaleString()}</b></span>
                      {item.actualAmount > item.plannedAmount && item.plannedAmount > 0 && <em>Over plan</em>}
                    </div>)}
                  </div>}
                  {budgetIntelligence.unconvertedByCurrency.length > 0 && <div className="multi-currency-warning">
                    <strong>Expenses not included in the converted total</strong>
                    {budgetIntelligence.unconvertedByCurrency.map(item => <div key={item.currencyCode}><span>{item.expenseCount} expenses · {item.currencyCode}</span><b>{item.originalAmount.toLocaleString()} {item.currencyCode}</b></div>)}
                    <small>Create or refresh the missing expense conversion snapshots to include these expenses. TravelOS does not estimate missing rates.</small>
                    {budgetIntelligence.unconvertedExpenses.slice(0,5).map(item => <small key={item.expenseId}>{item.title}: {item.reason}</small>)}
                  </div>}
                  <div className="intelligence-footer"><small>Converted values use saved historical reference-rate snapshots. Original expenses and settlements remain unchanged.</small><button type="button" className="expense-edit" disabled={loadingBudgetIntelligence} onClick={() => loadBudgetIntelligence(selectedTrip,budgetReportingCurrency)}>{loadingBudgetIntelligence ? "Refreshing…" : "Refresh budget intelligence"}</button></div>
                </> : <p className="budget-empty">{loadingBudgetIntelligence ? "Calculating multi-currency spending…" : "No multi-currency budget report is available yet."}</p>}
              </section>
              {selectedTrip.currentUserRole === "owner" && <form className="budget-cap-form" onSubmit={saveBudget}>
                <label>Trip budget cap<input type="number" min="0" step="0.01" value={budgetAmount} onChange={e => setBudgetAmount(e.target.value)} placeholder="Set your total budget" required /></label>
                <label>Budget currency<select value={budgetCurrency} onChange={e => setBudgetCurrency(e.target.value)}><option value="USD">USD</option><option value="LKR">LKR</option><option value="EUR">EUR</option><option value="GBP">GBP</option><option value="JPY">JPY</option><option value="AUD">AUD</option><option value="INR">INR</option><option value="SGD">SGD</option></select></label>
                <button className="btn btn-primary" disabled={busy}>{budget ? "Update budget" : "Set budget"}</button>
              </form>}
              {budget ? <>
                <div className="budget-summary"><div><span>YOUR BUDGET</span><strong>{budget.amount.toLocaleString()} {budget.currencyCode}</strong></div><div><span>PLANNED SPEND</span>{budget.totalsByCurrency.map(total => <strong key={total.currencyCode}>{total.plannedAmount.toLocaleString()} {total.currencyCode}</strong>)}</div></div>
                {budget.items.length > 0 ? <div className="budget-items">{budget.items.map(item => <div className="budget-item-row" key={item.id}><span className="budget-item-icon">↗</span><strong>{item.category}</strong><span>{item.amount.toLocaleString()} {item.currencyCode}</span>{selectedTrip.currentUserRole === "owner" && <button className="delete-link" onClick={() => deleteBudgetItem(item.id)}>Remove</button>}</div>)}</div> : <p className="budget-empty">No planned categories yet. Add accommodation, transport, food or activities.</p>}
                {selectedTrip.currentUserRole === "owner" && <form className="budget-item-form" onSubmit={addBudgetItem}>
                  <label>Category<input value={budgetCategory} onChange={e => setBudgetCategory(e.target.value)} placeholder="e.g. Accommodation" maxLength={80} required /></label>
                  <label>Planned amount<input type="number" min="0" step="0.01" value={budgetItemAmount} onChange={e => setBudgetItemAmount(e.target.value)} placeholder="0.00" required /></label>
                  <label>Currency<select value={budgetItemCurrency || budget.currencyCode} onChange={e => setBudgetItemCurrency(e.target.value)}><option value="USD">USD</option><option value="LKR">LKR</option><option value="EUR">EUR</option><option value="GBP">GBP</option><option value="JPY">JPY</option><option value="AUD">AUD</option><option value="INR">INR</option><option value="SGD">SGD</option></select></label>
                  <button className="btn btn-quiet" disabled={busy}>＋ Add item</button>
                </form>}
                <p className="budget-footnote">Amounts are planned estimates. Different currencies are shown separately; no exchange-rate conversion is applied.</p>
              </> : <p className="budget-empty">{selectedTrip.currentUserRole === "owner" ? "Set a budget cap to start tracking planned trip costs." : "The trip owner hasn't set a budget yet."}</p>}
            </section>

            {selectedTrip.currentUserRole === "owner" && <section className="ai-planner-card">
              <div className="ai-planner-heading"><div><p className="eyebrow">TRAVELOS INTELLIGENCE</p><h2>Start with an AI draft.</h2><p>Generate a suggested day-by-day plan, review it, then decide whether to apply it.</p></div><span className="ai-spark">✳</span></div>
              <form className="ai-options" onSubmit={generateAiDraft}>
                <label>Travellers<input type="number" min="1" max="30" value={travellerCount} onChange={e => setTravellerCount(Number(e.target.value))} /></label>
                <label>Interests<input value={interestsText} onChange={e => setInterestsText(e.target.value)} placeholder="Food, culture, nature…" /></label>
                <label>Travel pace<select value={travelPace} onChange={e => setTravelPace(e.target.value as "relaxed" | "balanced" | "fast")}><option value="relaxed">Relaxed</option><option value="balanced">Balanced</option><option value="fast">Fast-paced</option></select></label>
                <button className="btn btn-primary" disabled={busy}>{busy ? "Generating…" : "✳ Generate AI draft"}</button>
              </form>
              <p className="ai-disclaimer">AI suggestions can be inaccurate. Verify places, hours, routes and costs. Generation requires a configured AI provider.</p>
            </section>}
            {aiDrafts.length > 0 && <section className="ai-drafts-section">
              <div className="section-heading"><div><p className="eyebrow">REVIEW BEFORE APPLYING</p><h2>AI itinerary drafts</h2></div><span className="count-label">{aiDrafts.filter(d => d.status === "pending_review").length} TO REVIEW</span></div>
              {aiDrafts.map(draft => <article className="ai-draft-card" key={draft.id}>
                <div className="ai-draft-top"><div><span className="draft-provider">{draft.provider}{draft.model ? ` · ${draft.model}` : ""}</span><h3>{draft.itinerary.title}</h3></div><span className={draft.status === "applied" ? "role owner" : "role"}>{draft.status.replace("_", " ")}</span></div>
                <p className="ai-draft-summary">{draft.itinerary.summary}</p>
                <div className="ai-draft-days">{draft.itinerary.days.map(day => <div key={day.dayNumber}><strong>Day {day.dayNumber} · {formatDate(day.date)}</strong><span>{day.theme}</span><small>{day.activities.map(activity => activity.title).join(" · ")}</small></div>)}</div>
                {draft.warnings?.map((warning, index) => <p className="ai-warning" key={index}>ⓘ {warning}</p>)}
                {draft.status === "pending_review" && selectedTrip.currentUserRole === "owner" && <button className="btn btn-primary apply-draft-button" disabled={busy} onClick={() => applyAiDraft(draft.id)}>Apply this draft →</button>}
              </article>)}
            </section>}

            {loadingItinerary ? <section className="empty-state"><p>Loading your itinerary…</p></section> :
              !itinerary ? <section className="empty-state itinerary-empty"><div className="empty-icon">✳</div><h3>Your itinerary starts here.</h3><p>Create a day-by-day plan for {selectedTrip.destination}. Add places and activities, then approve it when you're happy.</p>{selectedTrip.currentUserRole === "owner" && <button className="btn btn-primary" disabled={busy} onClick={createItinerary}>Create itinerary →</button>}</section> :
              <>
                <section className="itinerary-toolbar">
                  <div><strong>{itinerary.days.length} planned days</strong><span>{itinerary.days.reduce((sum, day) => sum + day.activities.length, 0)} activities</span></div>
                  {canEditItinerary && <div className="toolbar-actions"><button className="btn btn-quiet" onClick={() => setShowAddDay(v => !v)}>＋ Add a day</button><button className="btn btn-primary" disabled={busy || itinerary.days.every(day => day.activities.length === 0)} onClick={approveItinerary}>Approve plan</button></div>}
                </section>
                {showAddDay && canEditItinerary && <form className="add-day-form" onSubmit={addDay}><label>Choose a date within your trip<input type="date" min={selectedTrip.startDate} max={selectedTrip.endDate} value={newDayDate} onChange={e => setNewDayDate(e.target.value)} required /></label><button className="btn btn-primary" disabled={busy || !newDayDate}>Add day</button><button type="button" className="btn btn-quiet" onClick={() => setShowAddDay(false)}>Cancel</button></form>}
                {itinerary.isApproved && <div className="approved-note">This itinerary has been approved and is now read-only.</div>}
                {itinerary.days.length === 0 ? <section className="empty-state"><h3>No days added yet.</h3><p>Add a date from your trip to start building your itinerary.</p></section> :
                  <section className="day-list">{itinerary.days.map(day => <article className="day-card" key={day.id}>
                    <div className="day-date"><span>DAY {day.dayNumber.toString().padStart(2, "0")}</span><strong>{formatDate(day.date)}</strong></div>
                    <div className="day-content">
                      {day.activities.some(activity => scheduleConflicts.has(activity.id)) && <p className="activity-schedule-warning">⚠ Scheduled activities overlap. Review their start and end times.</p>}
                      {day.activities.length === 0 ? <p className="no-activities">Nothing planned yet. Add your first activity.</p> :
                        <div className="activity-list">{day.activities.map(activity => editingActivityId === activity.id && canEditItinerary ?
                          <form className="activity-form activity-edit-form" key={activity.id} onSubmit={event => updateActivity(event,activity.id)}>
                            <label>Activity name<input value={activityTitle} onChange={e=>setActivityTitle(e.target.value)} maxLength={180} required /></label>
                            <label>Place<input value={activityPlace} onChange={e=>setActivityPlace(e.target.value)} maxLength={200} /></label>
                            <label className="wide-field">Notes<textarea value={activityDescription} onChange={e=>setActivityDescription(e.target.value)} maxLength={2000} rows={2}/></label>
                            <label>Start time<input type="time" value={activityStartTime} onChange={e=>setActivityStartTime(e.target.value)}/></label>
                            <label>End time<input type="time" value={activityEndTime} onChange={e=>setActivityEndTime(e.target.value)}/></label>
                            <label>Duration (minutes)<input type="number" min="1" value={activityDuration} onChange={e=>setActivityDuration(e.target.value)}/></label>
                            <label>Estimated cost ({selectedTrip.currencyCode})<input type="number" min="0" step="0.01" value={activityCost} onChange={e=>setActivityCost(e.target.value)}/></label>
                            <div className="form-actions wide-field"><button type="button" className="btn btn-quiet" onClick={()=>setEditingActivityId(null)}>Cancel</button><button className="btn btn-primary" disabled={busy}>{busy?"Saving…":"Save changes"}</button></div>
                          </form> :
                          <div className="activity-row" key={activity.id}>
                            <span className="activity-dot">•</span><div className="activity-info"><strong>{activity.title}</strong>{activity.placeName && <span>{activity.placeName}</span>}{activity.startTime && activity.endTime && <span className="activity-time">{activity.startTime.slice(0,5)} – {activity.endTime.slice(0,5)}</span>}{activity.description && <p>{activity.description}</p>}</div>
                            <div className="activity-meta">{activity.durationMinutes && <span>{activity.durationMinutes} min</span>}{activity.estimatedCost !== null && <span>{activity.estimatedCost.toLocaleString()} {activity.currencyCode}</span>}</div>
                            {canEditItinerary && <><button className="delete-link" onClick={()=>beginEditActivity(activity)}>Edit</button><button className="delete-link" onClick={()=>deleteActivity(activity.id)}>Remove</button></>}
                          </div>)}</div>}
                      {canEditItinerary && (activeDayId === day.id ?
                        <form className="activity-form" onSubmit={event => addActivity(event, day.id)}>
                          <label>Activity name<input value={activityTitle} onChange={e => setActivityTitle(e.target.value)} placeholder="e.g. Explore the old town" maxLength={180} required /></label>
                          <label>Place<input value={activityPlace} onChange={e => setActivityPlace(e.target.value)} placeholder="Place or attraction" maxLength={200} /></label>
                          <label className="wide-field">Notes<textarea value={activityDescription} onChange={e => setActivityDescription(e.target.value)} placeholder="A little detail about this stop…" maxLength={2000} rows={2} /></label>
                          <label>Start time<input type="time" value={activityStartTime} onChange={e => setActivityStartTime(e.target.value)} /></label>
                          <label>End time<input type="time" value={activityEndTime} onChange={e => setActivityEndTime(e.target.value)} /></label>
                          <label>Duration (minutes)<input type="number" min="1" value={activityDuration} onChange={e => setActivityDuration(e.target.value)} placeholder="90" /></label>
                          <label>Estimated cost ({selectedTrip.currencyCode})<input type="number" min="0" step="0.01" value={activityCost} onChange={e => setActivityCost(e.target.value)} placeholder="0.00" /></label>
                          <div className="form-actions wide-field"><button type="button" className="btn btn-quiet" onClick={() => setActiveDayId(null)}>Cancel</button><button className="btn btn-primary" disabled={busy}>{busy ? "Adding…" : "Save activity"}</button></div>
                        </form> :
                        <button className="add-activity-link" onClick={() => setActiveDayId(day.id)}>＋ Add activity</button>)}
                    </div>
                  </article>)}</section>}
              </>}
          </>}
          <footer className="dashboard-footer">TRAVELOS · THOUGHTFULLY CONNECTED TRAVEL</footer>
        </div>
      </section>
    </main>
  );
}
