import React, { useCallback, useState } from 'react';
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
import { InputChanged } from 'typings/inputs';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';

export interface AddMovieVersionModalContentProps {
  movieId: number;
  onVersionAdded(): void;
  onModalClose(): void;
}

function AddMovieVersionModalContent({
  movieId,
  onVersionAdded,
  onModalClose,
}: AddMovieVersionModalContentProps) {
  const movie = useMovie(movieId);

  const [qualityProfileId, setQualityProfileId] = useState(
    movie?.qualityProfileId ?? 0
  );
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

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
      url: '/movieversion',
      method: 'POST',
      dataType: 'json',
      data: JSON.stringify({
        movieId,
        qualityProfileId,
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
  }, [movieId, qualityProfileId, onVersionAdded]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddVersion')}</ModalHeader>

      <ModalBody>
        {saveError ? <Alert kind={kinds.DANGER}>{saveError}</Alert> : null}

        <Alert kind={kinds.INFO}>{translate('MovieVersionFolderInfo')}</Alert>

        <Form>
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
        </Form>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isSaving}
          onPress={handleAddPress}
        >
          {translate('Add')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddMovieVersionModalContent;
