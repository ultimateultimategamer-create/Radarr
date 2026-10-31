import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
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
import { inputTypes, kinds } from 'Helpers/Props';
import useMovie from 'Movie/useMovie';
import { fetchCustomFormats } from 'Store/Actions/settingsActions';
import { InputChanged } from 'typings/inputs';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import MovieVersion from './MovieVersion';

export interface AddMovieVersionModalContentProps {
  movieId: number;
  version?: MovieVersion;
  onVersionAdded(): void;
  onModalClose(): void;
}

function AddMovieVersionModalContent({
  movieId,
  version,
  onVersionAdded,
  onModalClose,
}: AddMovieVersionModalContentProps) {
  const movie = useMovie(movieId);
  const dispatch = useDispatch();
  const customFormats = useSelector(
    (state: AppState) => state.settings.customFormats.items
  );
  const [name, setName] = useState(version?.name ?? '');
  const [requiredCustomFormatIds, setRequiredCustomFormatIds] = useState(
    version?.requiredCustomFormatIds ?? []
  );

  const [qualityProfileId, setQualityProfileId] = useState(
    version?.qualityProfileId ?? movie?.qualityProfileId ?? 0
  );
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    dispatch(fetchCustomFormats());
  }, [dispatch]);

  const handleNameChange = useCallback(({ value }: InputChanged<string>) => {
    setName(value);
  }, []);

  const handleFormatsChange = useCallback(
    ({ value }: InputChanged<number[]>) => {
      setRequiredCustomFormatIds(value);
    },
    []
  );

  const handleQualityProfileChange = useCallback(
    ({ value }: InputChanged<string | number>) => {
      setQualityProfileId(Number(value));
    },
    []
  );

  const handleAddPress = useCallback(() => {
    setIsSaving(true);
    setSaveError(null);

    const { request } = createAjaxRequest({
      url: version ? `/movieversion/${version.id}` : '/movieversion',
      method: version ? 'PUT' : 'POST',
      dataType: 'json',
      data: JSON.stringify({
        id: version?.id,
        movieId,
        name: name.trim(),
        qualityProfileId,
        requiredCustomFormatIds,
      }),
    });

    request
      .done(() => {
        setIsSaving(false);
        onVersionAdded();
      })
      .fail((xhr: { responseJSON?: { message?: string } }) => {
        setIsSaving(false);
        setSaveError(xhr.responseJSON?.message ?? translate('Error'));
      });
  }, [
    movieId,
    name,
    qualityProfileId,
    requiredCustomFormatIds,
    version,
    onVersionAdded,
  ]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate(version ? 'EditMovieVersion' : 'AddVersion')}
      </ModalHeader>

      <ModalBody>
        {saveError ? <Alert kind={kinds.DANGER}>{saveError}</Alert> : null}

        <Alert kind={kinds.INFO}>{translate('MovieVersionFolderInfo')}</Alert>

        <Form>
          <FormGroup>
            <FormLabel>{translate('Name')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TEXT}
              name="name"
              value={name}
              helpText={translate('MovieVersionNameHelpText')}
              onChange={handleNameChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('QualityProfile')}</FormLabel>

            <FormInputGroup
              type={inputTypes.QUALITY_PROFILE_SELECT}
              name="qualityProfileId"
              value={qualityProfileId}
              helpText={translate('MovieVersionQualityProfileHelpText')}
              onChange={handleQualityProfileChange}
            />
          </FormGroup>

          <FormGroup>
            <FormLabel>{translate('MovieVersionRequiredFormats')}</FormLabel>

            <FormInputGroup
              type={inputTypes.SELECT}
              name="requiredCustomFormatIds"
              value={requiredCustomFormatIds}
              values={customFormats.map((format) => ({
                key: format.id,
                value: format.name,
              }))}
              helpText={translate('MovieVersionRequiredFormatsHelpText')}
              onChange={handleFormatsChange}
            />
          </FormGroup>
        </Form>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isSaving}
          isDisabled={!name.trim() || isSaving}
          onPress={handleAddPress}
        >
          {translate(version ? 'Save' : 'Add')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddMovieVersionModalContent;
