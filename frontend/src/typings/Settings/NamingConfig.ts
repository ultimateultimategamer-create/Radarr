type ColonReplacementFormat =
  | 'delete'
  | 'dash'
  | 'spaceDash'
  | 'spaceDashSpace'
  | 'smart';

export type ExtraVersionNaming = 'label' | 'plexEdition';

export default interface NamingConfig {
  renameMovies: boolean;
  replaceIllegalCharacters: boolean;
  colonReplacementFormat: ColonReplacementFormat;
  standardMovieFormat: string;
  movieFolderFormat: string;
  extraVersionNaming: ExtraVersionNaming;
}
