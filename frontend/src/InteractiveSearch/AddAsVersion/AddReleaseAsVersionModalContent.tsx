import React, { useCallback, useState } from 'react';
import { useDispatch } from 'react-redux';
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
import useMovieVersions from 'Movie/Details/Versions/useMovieVersions';
import { updateRelease } from 'Store/Actions/releaseActions';
import { InputChanged } from 'typings/inputs';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';

export interface AddReleaseAsVersionModalContentProps {
  guid: string;
  indexerId: number;
  movieId: number;
  title: string;
  suggestedName: string;
  onModalClose(): void;
}

// Grabs the release as a new, pinned version of the movie: it's stored next to the movie's other files,
// and Radarr never searches for, upgrades or replaces it on its own.
function AddReleaseAsVersionModalContent({
  guid,
  indexerId,
  movieId,
  title,
  suggestedName,
  onModalClose,
}: AddReleaseAsVersionModalContentProps) {
  const dispatch = useDispatch();
  const { refetchVersions } = useMovieVersions(movieId);
  const [name, setName] = useState(suggestedName);
  const [isGrabbing, setIsGrabbing] = useState(false);
  const [grabError, setGrabError] = useState<string | null>(null);

  const handleNameChange = useCallback(({ value }: InputChanged<string>) => {
    setName(value);
  }, []);

  const handleAddPress = useCallback(() => {
    setIsGrabbing(true);
    setGrabError(null);

    const { request } = createAjaxRequest({
      url: '/release',
      method: 'POST',
      dataType: 'json',
      contentType: 'application/json',
      data: JSON.stringify({
        guid,
        indexerId,
        movieId,
        addAsMovieVersion: true,
        movieVersionName: name.trim(),
      }),
    });

    request
      .done(() => {
        setIsGrabbing(false);

        dispatch(
          updateRelease({
            guid,
            isGrabbing: false,
            isGrabbed: true,
            grabError: null,
          })
        );

        refetchVersions();
        onModalClose();
      })
      .fail((xhr: { responseJSON?: { message?: string } }) => {
        setIsGrabbing(false);
        setGrabError(
          xhr.responseJSON?.message ?? translate('AddReleaseAsVersionError')
        );
      });
  }, [guid, indexerId, movieId, name, dispatch, refetchVersions, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('AddReleaseAsVersionModalTitle', { title })}
      </ModalHeader>

      <ModalBody>
        {grabError ? <Alert kind={kinds.DANGER}>{grabError}</Alert> : null}

        <Alert kind={kinds.INFO}>{translate('AddReleaseAsVersionInfo')}</Alert>

        <Form>
          <FormGroup>
            <FormLabel>{translate('Name')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TEXT}
              name="name"
              value={name}
              helpText={translate('PinnedMovieVersionNameHelpText')}
              onChange={handleNameChange}
            />
          </FormGroup>
        </Form>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerButton
          kind={kinds.SUCCESS}
          isSpinning={isGrabbing}
          isDisabled={!name.trim() || isGrabbing}
          onPress={handleAddPress}
        >
          {translate('AddAsNewVersion')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddReleaseAsVersionModalContent;
