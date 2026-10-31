import React, { useCallback, useState } from 'react';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import MovieInteractiveSearchModal from 'Movie/Search/MovieInteractiveSearchModal';
import QualityProfileName from 'Settings/Profiles/Quality/QualityProfileName';
import translate from 'Utilities/String/translate';
import MovieVersion from './MovieVersion';
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
  const { id, movieId, qualityProfileId, hasFile, isPrimary } = version;

  const [isInteractiveSearchModalOpen, setIsInteractiveSearchModalOpen] =
    useState(false);

  const handleRemovePress = useCallback(() => {
    onRemovePress(id);
  }, [id, onRemovePress]);

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
          <span className={styles.profile}>
            <QualityProfileName qualityProfileId={qualityProfileId} />
          </span>

          {isPrimary ? (
            <Label kind={kinds.INFO}>{translate('PrimaryVersion')}</Label>
          ) : null}

          {hasFile ? null : (
            <Label kind={kinds.DANGER}>{translate('Missing')}</Label>
          )}

          <div className={styles.actions}>
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
