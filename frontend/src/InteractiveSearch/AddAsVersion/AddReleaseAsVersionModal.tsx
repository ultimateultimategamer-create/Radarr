import React from 'react';
import Modal from 'Components/Modal/Modal';
import AddReleaseAsVersionModalContent, {
  AddReleaseAsVersionModalContentProps,
} from './AddReleaseAsVersionModalContent';

interface AddReleaseAsVersionModalProps
  extends AddReleaseAsVersionModalContentProps {
  isOpen: boolean;
}

function AddReleaseAsVersionModal({
  isOpen,
  ...otherProps
}: AddReleaseAsVersionModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={otherProps.onModalClose}>
      <AddReleaseAsVersionModalContent {...otherProps} />
    </Modal>
  );
}

export default AddReleaseAsVersionModal;
