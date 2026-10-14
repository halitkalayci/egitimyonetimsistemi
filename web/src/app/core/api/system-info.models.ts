/** Mirrors the SystemInfoDto the API returns (Application layer contract, never a domain entity). */
export interface SystemInfoEntry {
  readonly id: string;
  readonly key: string;
  readonly value: string;
  readonly recordedAtUtc: string;
}

export interface SystemInfo {
  readonly applicationName: string;
  readonly applicationTimeUtc: string;
  readonly databaseTimeUtc: string;
  readonly entries: readonly SystemInfoEntry[];
}
