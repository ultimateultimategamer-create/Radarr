import React, { useCallback, useState } from 'react';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import MovieInteractiveSearchModal from 'Movie/Search/MovieInteractiveSearchModal';
import QualityProfileName from 'Settings/Profiles/Quality/QualityProfileName';
import translate from 'Utilities/String/translate';
import AddMovieVersionModal from './AddMovieVersionModal';
import MovieVersion from './MovieVersion';
import useMovieVersions from './useMovieVersions';
import styles from './MovieVersionHeaderRow.css';

interface MovieVersionHeaderRowProps {
  version: MovieVersion;
  colSpan: number;
  onRemovePress(id: number): void;
}

function MovieVersionHeaderRow({
  version,
  colSpan,
  onRemovePress,
}: MovieVersionHeaderRowProps) {
  const { id, movieId, name, qualityProfileId, hasFile, isPrimary } = version;
  const { refetchVersions } = useMovieVersions(movieId);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);

  const [isInteractiveSearchModalOpen, setIsInteractiveSearchModalOpen] =
    useState(false);

  const handleRemovePress = useCallback(() => {
    onRemovePress(id);
  }, [id, onRemovePress]);

  const handleEditPress = useCallback(() => setIsEditModalOpen(true), []);
  const handleEditClose = useCallback(() => setIsEditModalOpen(false), []);
  const handleVersionSaved = useCallback(() => {
    setIsEditModalOpen(false);
    refetchVersions();
  }, [refetchVersions]);

  const handleInteractiveSearchPress = useCallback(() => {
    setIsInteractiveSearchModalOpen(true);
  }, []);

  const handleInteractiveSearchModalClose = useCallback(() => {
    setIsInteractiveSearchModalOpen(false);
  }, []);

  return (
    <TableRow className={styles.row}>
      <TableRowCell className={styles.cell} colSpan={colSpan}>
        <div className={styles.header}>
          <span className={styles.profile}>{name}</span>

          <QualityProfileName qualityProfileId={qualityProfileId} />

          {isPrimary ? (
            <Label kind={kinds.INFO}>{translate('PrimaryVersion')}</Label>
          ) : null}

          {hasFile ? null : (
            <Label kind={kinds.DANGER}>{translate('Missing')}</Label>
          )}

          <div className={styles.actions}>
            <IconButton
              name={icons.EDIT}
              title={translate('EditMovieVersion')}
              onPress={handleEditPress}
            />

            <IconButton
              name={icons.INTERACTIVE}
              title={translate('InteractiveSearchMovieVersion')}
              onPress={handleInteractiveSearchPress}
            />

            {isPrimary || hasFile ? null : (
              <IconButton
                name={icons.REMOVE}
                title={translate('RemoveVersion')}
                onPress={handleRemovePress}
              />
            )}
          </div>
        </div>

        <AddMovieVersionModal
          isOpen={isEditModalOpen}
          movieId={movieId}
          version={version}
          onVersionAdded={handleVersionSaved}
          onModalClose={handleEditClose}
        />

        <MovieInteractiveSearchModal
          isOpen={isInteractiveSearchModalOpen}
          movieId={movieId}
          movieVersionId={id}
          onModalClose={handleInteractiveSearchModalClose}
        />
      </TableRowCell>
    </TableRow>
  );
}

export default MovieVersionHeaderRow;
