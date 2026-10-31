import React, { useCallback, useMemo, useState } from 'react';
import { useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds, scrollDirections } from 'Helpers/Props';
import MovieVersion from 'Movie/Details/Versions/MovieVersion';
import { InputChanged } from 'typings/inputs';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';

// Adds a new, pinned version of the movie for the file
const NEW_VERSION_KEY = -1;

export interface SelectMovieVersionModalContentProps {
  movieId?: number;
  movieVersionId: number;
  movieVersions: MovieVersion[];
  modalTitle: string;
  onMovieVersionSelect(movieVersionId: number): void;
  onModalClose(): void;
}

// The primary version is sent as 0, like files that aren't attached to an additional version
export function getMovieVersionKey(version: MovieVersion) {
  return version.isPrimary ? 0 : version.id;
}

function SelectMovieVersionModalContent({
  movieId,
  movieVersions,
  modalTitle,
  onMovieVersionSelect,
  onModalClose,
  ...otherProps
}: SelectMovieVersionModalContentProps) {
  const [movieVersionId, setMovieVersionId] = useState(
    otherProps.movieVersionId
  );
  const [newVersionName, setNewVersionName] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const qualityProfiles = useSelector(
    (state: AppState) => state.settings.qualityProfiles.items
  );

  const isNewVersion = movieVersionId === NEW_VERSION_KEY;

  const values = useMemo(() => {
    const options = movieVersions.map((version) => {
      const profile = qualityProfiles.find(
        (p) => p.id === version.qualityProfileId
      );

      return {
        key: getMovieVersionKey(version),
        value: version.pinned
          ? version.name
          : `${version.name} (${profile?.name ?? translate('Unknown')})`,
        hint: version.isPrimary ? translate('PrimaryVersion') : undefined,
      };
    });

    if (movieId) {
      options.push({
        key: NEW_VERSION_KEY,
        value: translate('NewMovieVersion'),
        hint: undefined,
      });
    }

    return options;
  }, [movieId, movieVersions, qualityProfiles]);

  const handleChange = useCallback(
    ({ value }: InputChanged<string | number>) => {
      setMovieVersionId(Number(value));
    },
    []
  );

  const handleNewVersionNameChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setNewVersionName(value);
    },
    []
  );

  const handleSelectPress = useCallback(() => {
    if (!isNewVersion) {
      onMovieVersionSelect(movieVersionId);

      return;
    }

    setIsSaving(true);
    setSaveError(null);

    const { request } = createAjaxRequest({
      url: '/movieversion',
      method: 'POST',
      dataType: 'json',
      data: JSON.stringify({
        movieId,
        name: newVersionName.trim(),
        pinned: true,
      }),
    });

    request
      .done((data: MovieVersion) => {
        setIsSaving(false);
        onMovieVersionSelect(data.id);
      })
      .fail((xhr: { responseJSON?: { message?: string } }) => {
        setIsSaving(false);
        setSaveError(xhr.responseJSON?.message ?? translate('Error'));
      });
  }, [
    isNewVersion,
    movieId,
    movieVersionId,
    newVersionName,
    onMovieVersionSelect,
  ]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('SetMovieVersionModalTitle', { modalTitle })}
      </ModalHeader>

      <ModalBody scrollDirection={scrollDirections.NONE}>
        {saveError ? <Alert kind={kinds.DANGER}>{saveError}</Alert> : null}

        <Form>
          <FormGroup>
            <FormLabel>{translate('Version')}</FormLabel>

            <FormInputGroup
              type={inputTypes.SELECT}
              name="movieVersionId"
              value={movieVersionId}
              values={values}
              helpText={translate('MovieVersionHelpText')}
              onChange={handleChange}
            />
          </FormGroup>

          {isNewVersion ? (
            <FormGroup>
              <FormLabel>{translate('Name')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT}
                name="newVersionName"
                value={newVersionName}
                helpText={translate('PinnedMovieVersionNameHelpText')}
                onChange={handleNewVersionNameChange}
              />
            </FormGroup>
          ) : null}
        </Form>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerButton
          kind={kinds.SUCCESS}
          isSpinning={isSaving}
          isDisabled={isSaving || (isNewVersion && !newVersionName.trim())}
          onPress={handleSelectPress}
        >
          {translate('SetMovieVersion')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default SelectMovieVersionModalContent;
