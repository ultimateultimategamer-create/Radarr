interface MovieVersion {
  id: number;
  movieId: number;
  name: string;
  qualityProfileId: number;
  requiredCustomFormatIds: number[];
  path: string;
  movieFileId: number;
  hasFile: boolean;
  isPrimary: boolean;
}

export default MovieVersion;
